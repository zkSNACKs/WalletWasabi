using NBitcoin;
using NBitcoin.Protocol;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionBroadcasting;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.FeeRateEstimation;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using WalletWasabi.Tests.XunitConfiguration;
using WalletWasabi.Wallets;
using WalletWasabi.WebClients.Wasabi;
using Xunit;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.Wallets.FilterProcessor;

namespace WalletWasabi.Tests.RegressionTests;

/// <seealso cref="RegTestCollectionDefinition"/>
[Collection("RegTest collection")]
public class SendTests : IClassFixture<RegTestFixture>
{
	public SendTests(RegTestFixture regTestFixture)
	{
		RegTestFixture = regTestFixture;
	}

	private RegTestFixture RegTestFixture { get; }

	[Fact]
	public async Task SendTestsAsync()
	{
		await using RegTestSetup setup = await RegTestSetup.InitializeTestEnvironmentAsync(RegTestFixture, numberOfBlocksToGenerate: 1, nameof(SendTestsAsync));
		IRPCClient rpc = setup.RpcClient;
		Network network = setup.Network;
		BitcoinStore bitcoinStore = setup.BitcoinStore;
		ServiceConfiguration serviceConfiguration = setup.ServiceConfiguration;
		string password = setup.Password;

		bitcoinStore.IndexStore.NewFilters += setup.Wallet_NewFiltersProcessed;

		// Create the services.
		// 1. Create connection service.
		NodesGroup nodes = new(setup.Network, requirements: Constants.NodeRequirements);
		nodes.ConnectedNodes.Add(await RegTestFixture.BackendRegTestNode.CreateNewP2pNodeAsync());

		// 2. Create mempool service.

		Node node = await RegTestFixture.BackendRegTestNode.CreateNewP2pNodeAsync();
		node.Behaviors.Add(bitcoinStore.CreateUntrustedP2pBehavior());

		// 3. Create wasabi synchronizer service.
		await using WasabiHttpClientFactory httpClientFactory = new(torEndPoint: null, backendUriGetter: () => new Uri(RegTestFixture.BackendEndPoint));
		using WasabiSynchronizer synchronizer = new(period: TimeSpan.FromSeconds(3), 10000, bitcoinStore, httpClientFactory.SharedWasabiClient);
		using FeeRateEstimationUpdater feeProvider = new(TimeSpan.Zero, () => "BlockstreamInfo");
		using UnconfirmedTransactionChainProvider unconfirmedChainProvider = new(httpClientFactory);

		// 4. Create key manager service.
		var keyManager = KeyManager.CreateNew(out _, password, network);

		// 5. Create wallet service.
		var workDir = Common.GetWorkDir(nameof(SendTestsAsync));

		using MemoryCache cache = BitcoinFactory.CreateMemoryCache();
		await using SpecificNodeBlockProvider specificNodeBlockProvider = new(network, serviceConfiguration, httpClientFactory.TorEndpoint);

		using BlockDownloadService blockDownloadService = new(
			bitcoinStore.BlockRepository,
			[specificNodeBlockProvider],
			new P2PBlockProvider(network, nodes, httpClientFactory.IsTorEnabled));

		WalletFactory walletFactory = new(workDir, network, bitcoinStore, synchronizer, httpClientFactory.SharedWasabiClient, serviceConfiguration, feeProvider, blockDownloadService, unconfirmedChainProvider);
		WalletManager walletManager = new(network, workDir, new WalletDirectories(network, workDir), walletFactory);
		walletManager.Initialize();

		// Get some money, make it confirm.
		var key = keyManager.GetNextReceiveKey("foo label");
		var key2 = keyManager.GetNextReceiveKey("foo label");
		var txId = await rpc.SendToAddressAsync(key.GetP2wpkhAddress(network), Money.Coins(1m));
		Assert.NotNull(txId);
		await rpc.GenerateAsync(1);
		var txId2 = await rpc.SendToAddressAsync(key2.GetP2wpkhAddress(network), Money.Coins(1m));
		Assert.NotNull(txId2);
		await rpc.GenerateAsync(1);

		try
		{
			Interlocked.Exchange(ref setup.FiltersProcessedByWalletCount, 0);
			nodes.Connect(); // Start connection service.
			node.VersionHandshake(); // Start mempool service.
			await synchronizer.StartAsync(CancellationToken.None); // Start wasabi synchronizer service.
			await feeProvider.StartAsync(CancellationToken.None);

			// Start wallet and filter processing service
			using var wallet = await walletManager.AddAndStartWalletAsync(keyManager);

			// Wait until the filter our previous transaction is present.
			var blockCount = await rpc.GetBlockCountAsync();
			await setup.WaitForFiltersToBeProcessedAsync(TimeSpan.FromSeconds(120), blockCount);

			TransactionBroadcaster broadcaster = new(network, bitcoinStore, httpClientFactory, walletManager);
			broadcaster.Initialize(nodes, rpc);

			var waitCount = 0;
			while (wallet.Coins.Sum(x => x.Amount) == Money.Zero)
			{
				await Task.Delay(1000);
				waitCount++;
				if (waitCount >= 21)
				{
					Logger.LogInfo($"Funding transaction to the wallet '{wallet.WalletName}' did not arrive.");
					return; // Very rarely this test fails. I have no clue why. Probably because all these RegTests are interconnected, anyway let's not bother the CI with it.
				}
			}

			var scp = CreateSegwitScriptPubKey();
			var res2 = wallet.BuildTransaction(password, new PaymentIntent(scp, Money.Coins(0.05m), label: "foo"), FeeStrategy.CreateFromConfirmationTarget(5), allowUnconfirmed: false);

			Assert.NotNull(res2.Transaction);
			Assert.Single(res2.OuterWalletOutputs);
			Assert.Equal(scp, res2.OuterWalletOutputs.Single().ScriptPubKey);
			Assert.Single(res2.InnerWalletOutputs);
			Assert.True(res2.Fee > Money.Satoshis(2 * 100)); // since there is a sanity check of 2sat/vb in the server
			Assert.InRange(res2.FeePercentOfSent, 0, 1);
			Assert.Single(res2.SpentCoins);
			var spentCoin = Assert.Single(res2.SpentCoins);
			Assert.Contains(new[] { key.P2wpkhScript, key2.P2wpkhScript }, x => x == spentCoin.ScriptPubKey);
			Assert.Equal(Money.Coins(1m), res2.SpentCoins.Single().Amount);
			Assert.False(res2.SpendsUnconfirmed);

			await broadcaster.SendTransactionAsync(res2.Transaction);

			Assert.Contains(res2.InnerWalletOutputs.Single(), wallet.Coins);

			//// Basic

			Script receive = keyManager.GetNextReceiveKey("Basic").P2wpkhScript;
			Money amountToSend = wallet.Coins.Where(x => x.IsAvailable()).Sum(x => x.Amount) / 2;
			var res = wallet.BuildTransaction(password, new PaymentIntent(receive, amountToSend, label: "foo"), FeeStrategy.SevenDaysConfirmationTargetStrategy, allowUnconfirmed: true);

			foreach (SmartCoin coin in res.SpentCoins)
			{
				Assert.False(coin.CoinJoinInProgress);
				Assert.True(coin.Confirmed);
				Assert.Null(coin.SpenderTransaction);
				Assert.False(coin.IsSpent());
			}

			Assert.Equal(2, res.InnerWalletOutputs.Count());
			Assert.Empty(res.OuterWalletOutputs);
			var activeOutput = res.InnerWalletOutputs.Single(x => x.ScriptPubKey == receive);
			var changeOutput = res.InnerWalletOutputs.Single(x => x.ScriptPubKey != receive);

			Assert.Equal(receive, activeOutput.ScriptPubKey);
			Assert.Equal(amountToSend, activeOutput.Amount);
			if (res.SpentCoins.Sum(x => x.Amount) - activeOutput.Amount == res.Fee) // this happens when change is too small
			{
				Assert.Contains(res.Transaction.Transaction.Outputs, x => x.Value == activeOutput.Amount);
				Logger.LogInfo($"Change Output: {changeOutput.Amount.ToString(false, true)} {changeOutput.ScriptPubKey.GetDestinationAddress(network)}");
			}
			Logger.LogInfo($"{nameof(res.Fee)}: {res.Fee}");
			Logger.LogInfo($"{nameof(res.FeePercentOfSent)}: {res.FeePercentOfSent} %");
			Logger.LogInfo($"{nameof(res.SpendsUnconfirmed)}: {res.SpendsUnconfirmed}");
			Logger.LogInfo($"Active Output: {activeOutput.Amount.ToString(false, true)} {activeOutput.ScriptPubKey.GetDestinationAddress(network)}");
			Logger.LogInfo($"TxId: {res.Transaction.GetHash()}");

			var foundReceive = false;
			Assert.InRange(res.Transaction.Transaction.Outputs.Count, 1, 2);
			foreach (var output in res.Transaction.Transaction.Outputs)
			{
				if (output.ScriptPubKey == receive)
				{
					foundReceive = true;
					Assert.Equal(amountToSend, output.Value);
				}
			}
			Assert.True(foundReceive);

			await broadcaster.SendTransactionAsync(res.Transaction);

			//// SubtractFeeFromAmount

			receive = keyManager.GetNextReceiveKey("SubtractFeeFromAmount").P2wpkhScript;
			amountToSend = wallet.Coins.Where(x => x.IsAvailable()).Sum(x => x.Amount) / 3;
			res = wallet.BuildTransaction(password, new PaymentIntent(receive, amountToSend, subtractFee: true, label: "foo"), FeeStrategy.SevenDaysConfirmationTargetStrategy, allowUnconfirmed: true);

			Assert.Equal(2, res.InnerWalletOutputs.Count());
			Assert.Empty(res.OuterWalletOutputs);
			activeOutput = res.InnerWalletOutputs.Single(x => x.ScriptPubKey == receive);
			changeOutput = res.InnerWalletOutputs.Single(x => x.ScriptPubKey != receive);

			Assert.Equal(receive, activeOutput.ScriptPubKey);
			Assert.Equal(amountToSend - res.Fee, activeOutput.Amount);
			Assert.Contains(res.Transaction.Transaction.Outputs, x => x.Value == changeOutput.Amount);
			Logger.LogInfo($"{nameof(res.Fee)}: {res.Fee}");
			Logger.LogInfo($"{nameof(res.FeePercentOfSent)}: {res.FeePercentOfSent} %");
			Logger.LogInfo($"{nameof(res.SpendsUnconfirmed)}: {res.SpendsUnconfirmed}");
			Logger.LogInfo($"Active Output: {activeOutput.Amount.ToString(false, true)} {activeOutput.ScriptPubKey.GetDestinationAddress(network)}");
			Logger.LogInfo($"Change Output: {changeOutput.Amount.ToString(false, true)} {changeOutput.ScriptPubKey.GetDestinationAddress(network)}");
			Logger.LogInfo($"TxId: {res.Transaction.GetHash()}");

			foundReceive = false;
			Assert.InRange(res.Transaction.Transaction.Outputs.Count, 1, 2);
			foreach (var output in res.Transaction.Transaction.Outputs)
			{
				if (output.ScriptPubKey == receive)
				{
					foundReceive = true;
					Assert.Equal(amountToSend - res.Fee, output.Value);
				}
			}
			Assert.True(foundReceive);

			//// LowFee

			res = wallet.BuildTransaction(password, new PaymentIntent(receive, amountToSend, label: "foo"), FeeStrategy.SevenDaysConfirmationTargetStrategy, allowUnconfirmed: true);

			Assert.Equal(2, res.InnerWalletOutputs.Count());
			Assert.Empty(res.OuterWalletOutputs);
			activeOutput = res.InnerWalletOutputs.Single(x => x.ScriptPubKey == receive);
			changeOutput = res.InnerWalletOutputs.Single(x => x.ScriptPubKey != receive);

			Assert.Equal(receive, activeOutput.ScriptPubKey);
			Assert.Equal(amountToSend, activeOutput.Amount);
			Assert.Contains(res.Transaction.Transaction.Outputs, x => x.Value == changeOutput.Amount);
			Logger.LogInfo($"{nameof(res.Fee)}: {res.Fee}");
			Logger.LogInfo($"{nameof(res.FeePercentOfSent)}: {res.FeePercentOfSent} %");
			Logger.LogInfo($"{nameof(res.SpendsUnconfirmed)}: {res.SpendsUnconfirmed}");
			Logger.LogInfo($"Active Output: {activeOutput.Amount.ToString(false, true)} {activeOutput.ScriptPubKey.GetDestinationAddress(network)}");
			Logger.LogInfo($"Change Output: {changeOutput.Amount.ToString(false, true)} {changeOutput.ScriptPubKey.GetDestinationAddress(network)}");
			Logger.LogInfo($"TxId: {res.Transaction.GetHash()}");

			foundReceive = false;
			Assert.InRange(res.Transaction.Transaction.Outputs.Count, 1, 2);
			foreach (var output in res.Transaction.Transaction.Outputs)
			{
				if (output.ScriptPubKey == receive)
				{
					foundReceive = true;
					Assert.Equal(amountToSend, output.Value);
				}
			}
			Assert.True(foundReceive);

			//// MediumFee

			res = wallet.BuildTransaction(password, new PaymentIntent(receive, amountToSend, label: "foo"), FeeStrategy.OneDayConfirmationTargetStrategy, allowUnconfirmed: true);

			Assert.Equal(2, res.InnerWalletOutputs.Count());
			Assert.Empty(res.OuterWalletOutputs);
			activeOutput = res.InnerWalletOutputs.Single(x => x.ScriptPubKey == receive);
			changeOutput = res.InnerWalletOutputs.Single(x => x.ScriptPubKey != receive);

			Assert.Equal(receive, activeOutput.ScriptPubKey);
			Assert.Equal(amountToSend, activeOutput.Amount);
			Assert.Contains(res.Transaction.Transaction.Outputs, x => x.Value == changeOutput.Amount);
			Logger.LogInfo($"{nameof(res.Fee)}: {res.Fee}");
			Logger.LogInfo($"{nameof(res.FeePercentOfSent)}: {res.FeePercentOfSent} %");
			Logger.LogInfo($"{nameof(res.SpendsUnconfirmed)}: {res.SpendsUnconfirmed}");
			Logger.LogInfo($"Active Output: {activeOutput.Amount.ToString(false, true)} {activeOutput.ScriptPubKey.GetDestinationAddress(network)}");
			Logger.LogInfo($"Change Output: {changeOutput.Amount.ToString(false, true)} {changeOutput.ScriptPubKey.GetDestinationAddress(network)}");
			Logger.LogInfo($"TxId: {res.Transaction.GetHash()}");

			foundReceive = false;
			Assert.InRange(res.Transaction.Transaction.Outputs.Count, 1, 2);
			foreach (var output in res.Transaction.Transaction.Outputs)
			{
				if (output.ScriptPubKey == receive)
				{
					foundReceive = true;
					Assert.Equal(amountToSend, output.Value);
				}
			}
			Assert.True(foundReceive);

			//// HighFee

			res = wallet.BuildTransaction(password, new PaymentIntent(receive, amountToSend, label: "foo"), FeeStrategy.TwentyMinutesConfirmationTargetStrategy, allowUnconfirmed: true);

			Assert.Equal(2, res.InnerWalletOutputs.Count());
			Assert.Empty(res.OuterWalletOutputs);
			activeOutput = res.InnerWalletOutputs.Single(x => x.ScriptPubKey == receive);
			changeOutput = res.InnerWalletOutputs.Single(x => x.ScriptPubKey != receive);

			Assert.Equal(receive, activeOutput.ScriptPubKey);
			Assert.Equal(amountToSend, activeOutput.Amount);
			Assert.Contains(res.Transaction.Transaction.Outputs, x => x.Value == changeOutput.Amount);
			Logger.LogInfo($"{nameof(res.Fee)}: {res.Fee}");
			Logger.LogInfo($"{nameof(res.FeePercentOfSent)}: {res.FeePercentOfSent} %");
			Logger.LogInfo($"{nameof(res.SpendsUnconfirmed)}: {res.SpendsUnconfirmed}");
			Logger.LogInfo($"Active Output: {activeOutput.Amount.ToString(false, true)} {activeOutput.ScriptPubKey.GetDestinationAddress(network)}");
			Logger.LogInfo($"Change Output: {changeOutput.Amount.ToString(false, true)} {changeOutput.ScriptPubKey.GetDestinationAddress(network)}");
			Logger.LogInfo($"TxId: {res.Transaction.GetHash()}");

			foundReceive = false;
			Assert.InRange(res.Transaction.Transaction.Outputs.Count, 1, 2);
			foreach (var output in res.Transaction.Transaction.Outputs)
			{
				if (output.ScriptPubKey == receive)
				{
					foundReceive = true;
					Assert.Equal(amountToSend, output.Value);
				}
			}
			Assert.True(foundReceive);

			Assert.InRange(res.Fee, Money.Zero, res.Fee);
			Assert.InRange(res.Fee, res.Fee, res.Fee);

			await broadcaster.SendTransactionAsync(res.Transaction);

			//// MaxAmount

			receive = keyManager.GetNextReceiveKey("MaxAmount").P2wpkhScript;

			res = wallet.BuildTransaction(password, new PaymentIntent(receive, MoneyRequest.CreateAllRemaining(), "foo"), FeeStrategy.SevenDaysConfirmationTargetStrategy, allowUnconfirmed: true);

			Assert.Single(res.InnerWalletOutputs);
			Assert.Empty(res.OuterWalletOutputs);
			activeOutput = res.InnerWalletOutputs.Single();

			Assert.Equal(receive, activeOutput.ScriptPubKey);

			Assert.Single(res.Transaction.Transaction.Outputs);
			var maxBuiltTxOutput = res.Transaction.Transaction.Outputs.Single();
			Assert.Equal(receive, maxBuiltTxOutput.ScriptPubKey);
			Assert.Equal(wallet.Coins.Where(x => x.IsAvailable()).Sum(x => x.Amount) - res.Fee, maxBuiltTxOutput.Value);

			await broadcaster.SendTransactionAsync(res.Transaction);

			//// InputSelection

			receive = keyManager.GetNextReceiveKey("InputSelection").P2wpkhScript;

			var inputCountBefore = res.SpentCoins.Count();

			res = wallet.BuildTransaction(
				password,
				new PaymentIntent(receive, MoneyRequest.CreateAllRemaining(), "foo"),
				FeeStrategy.SevenDaysConfirmationTargetStrategy,
				allowUnconfirmed: true,
				allowedInputs: wallet.Coins.Where(x => x.IsAvailable()).Select(x => x.Outpoint).Take(1));

			Assert.Single(res.InnerWalletOutputs);
			Assert.Empty(res.OuterWalletOutputs);
			activeOutput = res.InnerWalletOutputs.Single(x => x.ScriptPubKey == receive);

			Assert.True(inputCountBefore >= res.SpentCoins.Count());
			Assert.Equal(res.SpentCoins.Count(), res.Transaction.Transaction.Inputs.Count);

			Assert.Equal(receive, activeOutput.ScriptPubKey);
			Logger.LogInfo($"{nameof(res.Fee)}: {res.Fee}");
			Logger.LogInfo($"{nameof(res.FeePercentOfSent)}: {res.FeePercentOfSent} %");
			Logger.LogInfo($"{nameof(res.SpendsUnconfirmed)}: {res.SpendsUnconfirmed}");
			Logger.LogInfo($"Active Output: {activeOutput.Amount.ToString(false, true)} {activeOutput.ScriptPubKey.GetDestinationAddress(network)}");
			Logger.LogInfo($"TxId: {res.Transaction.GetHash()}");

			Assert.Single(res.Transaction.Transaction.Outputs);

			res = wallet.BuildTransaction(
				password,
				new PaymentIntent(receive, MoneyRequest.CreateAllRemaining(), "foo"),
				FeeStrategy.SevenDaysConfirmationTargetStrategy,
				allowUnconfirmed: true,
				allowedInputs: new[] { res.SpentCoins.Select(x => x.Outpoint).First() });

			Assert.Single(res.InnerWalletOutputs);
			Assert.Empty(res.OuterWalletOutputs);
			activeOutput = res.InnerWalletOutputs.Single(x => x.ScriptPubKey == receive);

			Assert.Single(res.Transaction.Transaction.Inputs);
			Assert.Single(res.Transaction.Transaction.Outputs);
			Assert.Single(res.SpentCoins);

			//// Labeling

			Script receive2 = keyManager.GetNextReceiveKey("foo").P2wpkhScript;
			res = wallet.BuildTransaction(password, new PaymentIntent(receive2, MoneyRequest.CreateAllRemaining(), "my label"), FeeStrategy.SevenDaysConfirmationTargetStrategy, allowUnconfirmed: true);

			// New labels will be added to the HdPubKey only when tx will be successfully broadcasted.
			Assert.Equal("foo, my label", res.HdPubKeysWithNewLabels.Values.Single());
			Assert.Single(res.InnerWalletOutputs);
			Assert.Equal("foo", res.InnerWalletOutputs.Single().HdPubKey.Labels);

			using Key keyLabeling1 = new();
			using Key keyLabeling2 = new();
			amountToSend = wallet.Coins.Where(x => x.IsAvailable()).Sum(x => x.Amount) / 3;
			res = wallet.BuildTransaction(
				password,
				new PaymentIntent(
					new DestinationRequest(keyLabeling1, amountToSend, labels: "outgoing"),
					new DestinationRequest(keyLabeling2, amountToSend, labels: "outgoing2")),
				FeeStrategy.SevenDaysConfirmationTargetStrategy,
				allowUnconfirmed: true);

			Assert.Single(res.InnerWalletOutputs);
			Assert.Equal(2, res.OuterWalletOutputs.Count());
			IEnumerable<string> la = res.HdPubKeysWithNewLabels.Values.Single().Select(x => x);
			Assert.Contains("outgoing", la);
			Assert.Contains("outgoing2", la);
			IEnumerable<string> change = res.InnerWalletOutputs.Single().HdPubKey.Labels;
			Assert.Empty(change);

			await broadcaster.SendTransactionAsync(res.Transaction);
			wallet.UpdateUsedHdPubKeysLabels(res.HdPubKeysWithNewLabels);

			IEnumerable<SmartCoin> unconfirmedCoins = wallet.Coins.Where(x => x.Height == Height.Mempool).ToArray();
			IEnumerable<string> unconfirmedCoinLabels = unconfirmedCoins.SelectMany(x => x.HdPubKey.Labels).ToArray();
			Assert.Contains("outgoing", unconfirmedCoinLabels);
			Assert.Contains("outgoing2", unconfirmedCoinLabels);
			IEnumerable<string> allKeyLabels = keyManager.GetKeys().SelectMany(x => x.Labels);
			Assert.Contains("outgoing", allKeyLabels);
			Assert.Contains("outgoing2", allKeyLabels);

			Interlocked.Exchange(ref setup.FiltersProcessedByWalletCount, 0);
			await rpc.GenerateAsync(1);
			await setup.WaitForFiltersToBeProcessedAsync(TimeSpan.FromSeconds(120), 1);

			var bestHeight = new Height(bitcoinStore.SmartHeaderChain.TipHeight);
			IEnumerable<string> confirmedCoinLabels = wallet.Coins.Where(x => x.Height == bestHeight).SelectMany(x => x.HdPubKey.Labels);
			Assert.Contains("outgoing", confirmedCoinLabels);
			Assert.Contains("outgoing2", confirmedCoinLabels);
			allKeyLabels = keyManager.GetKeys().SelectMany(x => x.Labels);
			Assert.Contains("outgoing", allKeyLabels);
			Assert.Contains("outgoing2", allKeyLabels);

			//// AllowedInputsDisallowUnconfirmed

			inputCountBefore = res.SpentCoins.Count();

			receive = keyManager.GetNextReceiveKey("AllowedInputsDisallowUnconfirmed").P2wpkhScript;

			var allowedInputs = wallet.Coins.Where(x => x.IsAvailable()).Select(x => x.Outpoint).Take(1);
			PaymentIntent toSend = new(receive, MoneyRequest.CreateAllRemaining(), "fizz");

			// covers:
			// disallow unconfirmed with allowed inputs
			res = wallet.BuildTransaction(password, toSend, FeeStrategy.TwentyMinutesConfirmationTargetStrategy, false, allowedInputs: allowedInputs);

			activeOutput = res.InnerWalletOutputs.Single(x => x.ScriptPubKey == receive);
			Assert.Single(res.InnerWalletOutputs);
			Assert.Empty(res.OuterWalletOutputs);

			Assert.Equal(receive, activeOutput.ScriptPubKey);
			Logger.LogDebug($"{nameof(res.Fee)}: {res.Fee}");
			Logger.LogDebug($"{nameof(res.FeePercentOfSent)}: {res.FeePercentOfSent} %");
			Logger.LogDebug($"{nameof(res.SpendsUnconfirmed)}: {res.SpendsUnconfirmed}");
			Logger.LogDebug($"Active Output: {activeOutput.Amount.ToString(false, true)} {activeOutput.ScriptPubKey.GetDestinationAddress(network)}");
			Logger.LogDebug($"TxId: {res.Transaction.GetHash()}");

			Assert.True(inputCountBefore >= res.SpentCoins.Count());
			Assert.False(res.SpendsUnconfirmed);

			Assert.Single(res.Transaction.Transaction.Inputs);
			Assert.Single(res.Transaction.Transaction.Outputs);
			Assert.Single(res.SpentCoins);

			Assert.True(inputCountBefore >= res.SpentCoins.Count());
			Assert.Equal(res.SpentCoins.Count(), res.Transaction.Transaction.Inputs.Count);

			//// CustomChange

			// covers:
			// custom change
			// feePc > 1
			var scp1 = CreateSegwitScriptPubKey();
			var scp2 = CreateSegwitScriptPubKey();
			res = wallet.BuildTransaction(
				password,
				new PaymentIntent(
					new DestinationRequest(scp1, MoneyRequest.CreateChange()),
					new DestinationRequest(scp2, Money.Coins(0.0003m), labels: "outgoing")),
				FeeStrategy.TwentyMinutesConfirmationTargetStrategy);

			Assert.Contains(scp1, res.OuterWalletOutputs.Select(x => x.ScriptPubKey));
			Assert.Contains(scp2, res.OuterWalletOutputs.Select(x => x.ScriptPubKey));

			//// FeePcHigh

			using Key keyFeePcHigh1 = new();
			res = wallet.BuildTransaction(
				password,
				new PaymentIntent(keyFeePcHigh1, Money.Coins(0.0003m), label: "outgoing"),
				FeeStrategy.TwentyMinutesConfirmationTargetStrategy);

			Assert.True(res.FeePercentOfSent > 1);

			using Key keyFeePcHigh2 = new();
			var newChangeK = keyManager.GenerateNewKey("foo", KeyState.Clean, isInternal: true);
			res = wallet.BuildTransaction(
				password,
				new PaymentIntent(
					new DestinationRequest(newChangeK.P2wpkhScript, MoneyRequest.CreateChange(), "boo"),
					new DestinationRequest(keyFeePcHigh2, Money.Coins(0.0003m), labels: "outgoing")),
				FeeStrategy.TwentyMinutesConfirmationTargetStrategy);

			Assert.True(res.FeePercentOfSent > 1);
			Assert.Single(res.OuterWalletOutputs);
			Assert.Single(res.InnerWalletOutputs);
			SmartCoin changeRes = res.InnerWalletOutputs.Single();
			Assert.Equal(newChangeK.P2wpkhScript, changeRes.ScriptPubKey);
			Assert.Equal(newChangeK.Labels, changeRes.HdPubKey.Labels);
			Assert.Equal(KeyState.Clean, newChangeK.KeyState); // Still clean, because the tx wasn't yet propagated.
		}
		finally
		{
			bitcoinStore.IndexStore.NewFilters -= setup.Wallet_NewFiltersProcessed;
			await walletManager.RemoveAndStopAllAsync(CancellationToken.None);
			await synchronizer.StopAsync(CancellationToken.None);
			await feeProvider.StopAsync(CancellationToken.None);
			nodes?.Dispose();
			node?.Disconnect();
			unconfirmedChainProvider?.Dispose();
		}
	}

	private static Script CreateSegwitScriptPubKey()
	{
		using Key key = new(); // We can dispose because Script is a sequence of bytes really.
		return key.GetScriptPubKey(ScriptPubKeyType.Segwit);
	}
}

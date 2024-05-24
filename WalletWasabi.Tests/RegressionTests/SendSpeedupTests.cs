using Microsoft.Extensions.Caching.Memory;
using NBitcoin.Protocol;
using NBitcoin;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionBroadcasting;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Models;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.Tests.XunitConfiguration;
using WalletWasabi.Wallets;
using WalletWasabi.WebClients.Wasabi;
using Xunit;
using WalletWasabi.Helpers;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Exceptions;
using WalletWasabi.FeeRateEstimation;
using WalletWasabi.Wallets.FilterProcessor;

namespace WalletWasabi.Tests.RegressionTests;

/// <seealso cref="RegTestCollectionDefinition"/>
[Collection("RegTest collection")]
public class SendSpeedupTests : IClassFixture<RegTestFixture>
{
	public SendSpeedupTests(RegTestFixture regTestFixture)
	{
		RegTestFixture = regTestFixture;
	}

	private RegTestFixture RegTestFixture { get; }

	[Fact]
	public async Task SendSpeedupTestsAsync()
	{
		await using RegTestSetup setup = await RegTestSetup.InitializeTestEnvironmentAsync(RegTestFixture, numberOfBlocksToGenerate: 1, nameof(SendSpeedupTestsAsync));
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
		var workDir = Common.GetWorkDir(nameof(SendSpeedupTestsAsync));

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
		var key = keyManager.GetNextReceiveKey("foo");
		var txId = await rpc.SendToAddressAsync(key.GetP2wpkhAddress(network), Money.Coins(1m));
		Assert.NotNull(txId);
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
			wallet.Kitchen.Cook(password);

			TransactionBroadcaster broadcaster = new(network, bitcoinStore, httpClientFactory, walletManager);
			broadcaster.Initialize(nodes, rpc);

			var waitCount = 0;
			while (wallet.Coins.Sum(x => x.Amount) == Money.Zero)
			{
				await Task.Delay(1000);
				waitCount++;
				if (waitCount >= 21)
				{
					throw new InvalidOperationException($"Funding transaction to the wallet '{wallet.WalletName}' did not arrive.");
				}
			}

			#region HasChange

			Money amountToSend = wallet.Coins.Where(x => x.IsAvailable()).Sum(x => x.Amount) / 2;
			var rpcAddress = await rpc.GetNewAddressAsync();
			var txToSpeedUp = wallet.BuildTransaction(password, new PaymentIntent(rpcAddress, amountToSend, label: "bar"), FeeStrategy.SevenDaysConfirmationTargetStrategy, allowUnconfirmed: true);
			await broadcaster.SendTransactionAsync(txToSpeedUp.Transaction);

			Assert.Equal(Assert.Single(txToSpeedUp.SpentCoins).SpenderTransaction, txToSpeedUp.Transaction);

			var rbf = wallet.SpeedUpTransaction(txToSpeedUp.Transaction);

			// Spender is not updated until broadcast:
			Assert.Equal(Assert.Single(txToSpeedUp.SpentCoins).SpenderTransaction, txToSpeedUp.Transaction);
			Assert.Equal(Assert.Single(rbf.SpentCoins).SpenderTransaction, txToSpeedUp.Transaction);

			await broadcaster.SendTransactionAsync(rbf.Transaction);

			Assert.False(wallet.BitcoinStore.TransactionStore.TryGetTransaction(txToSpeedUp.Transaction.GetHash(), out _));

			Assert.Equal(Assert.Single(txToSpeedUp.SpentCoins), Assert.Single(rbf.SpentCoins));
			Assert.Equal(Assert.Single(txToSpeedUp.SpentCoins).SpenderTransaction, rbf.Transaction);
			Assert.Equal(Assert.Single(rbf.SpentCoins).SpenderTransaction, rbf.Transaction);

			var txToSpeedUpChange = Assert.Single(txToSpeedUp.InnerWalletOutputs);
			var rbfChange = Assert.Single(rbf.InnerWalletOutputs);
			Assert.Equal(txToSpeedUpChange.ScriptPubKey, rbfChange.ScriptPubKey);
			Assert.True(txToSpeedUpChange.Amount > rbfChange.Amount);

			var txToSpeedUpForeignOutput = Assert.Single(txToSpeedUp.OuterWalletOutputs);
			var rbfForeignOutput = Assert.Single(rbf.OuterWalletOutputs);
			Assert.Equal(txToSpeedUpForeignOutput.Amount, rbfForeignOutput.Amount);
			Assert.Equal(txToSpeedUpForeignOutput.ScriptPubKey, rbfForeignOutput.ScriptPubKey);

			Assert.True(rbf.Fee > txToSpeedUp.Fee);

			Assert.False(txToSpeedUp.SpendsUnconfirmed);
			Assert.False(rbf.SpendsUnconfirmed);

			Assert.Empty(txToSpeedUp.Transaction.ForeignInputs);
			Assert.Empty(rbf.Transaction.ForeignInputs);

			Assert.False(txToSpeedUp.Transaction.IsReplacement);
			Assert.False(txToSpeedUp.Transaction.IsCPFP);
			Assert.False(txToSpeedUp.Transaction.IsCPFPd);
			Assert.Empty(txToSpeedUp.Transaction.ParentsThisTxPaysFor);
			Assert.Empty(txToSpeedUp.Transaction.ChildrenPayForThisTx);
			Assert.False(txToSpeedUp.Transaction.IsSpeedup);
			Assert.False(txToSpeedUp.Transaction.IsCancellation);
			Assert.True(rbf.Transaction.IsReplacement);
			Assert.False(rbf.Transaction.IsCPFP);
			Assert.False(rbf.Transaction.IsCPFPd);
			Assert.Empty(rbf.Transaction.ParentsThisTxPaysFor);
			Assert.Empty(rbf.Transaction.ChildrenPayForThisTx);
			Assert.True(rbf.Transaction.IsSpeedup);
			Assert.False(rbf.Transaction.IsCancellation);

			Assert.Equal("bar", txToSpeedUp.Transaction.Labels);
			Assert.Equal("bar", rbf.Transaction.Labels);
			Assert.Empty(txToSpeedUp.Transaction.WalletOutputs.SelectMany(x => x.HdPubKey.Labels));
			Assert.Empty(rbf.Transaction.WalletOutputs.SelectMany(x => x.HdPubKey.Labels));

			#endregion HasChange

			#region CanDoTwiceHasChange

			var rbf2 = wallet.SpeedUpTransaction(rbf.Transaction);

			// Spender is not updated until broadcast:
			Assert.Equal(Assert.Single(rbf.SpentCoins).SpenderTransaction, rbf.Transaction);
			Assert.Equal(Assert.Single(rbf2.SpentCoins).SpenderTransaction, rbf.Transaction);

			await broadcaster.SendTransactionAsync(rbf2.Transaction);

			Assert.False(wallet.BitcoinStore.TransactionStore.TryGetTransaction(rbf.Transaction.GetHash(), out _));

			Assert.Equal(Assert.Single(rbf.SpentCoins), Assert.Single(rbf2.SpentCoins));
			Assert.Equal(Assert.Single(rbf.SpentCoins).SpenderTransaction, rbf2.Transaction);
			Assert.Equal(Assert.Single(rbf2.SpentCoins).SpenderTransaction, rbf2.Transaction);

			var rbf2Change = Assert.Single(rbf2.InnerWalletOutputs);
			Assert.Equal(rbfChange.ScriptPubKey, rbf2Change.ScriptPubKey);
			Assert.True(rbfChange.Amount > rbf2Change.Amount);

			var rbf2ForeignOutput = Assert.Single(rbf2.OuterWalletOutputs);
			Assert.Equal(rbfForeignOutput.Amount, rbf2ForeignOutput.Amount);
			Assert.Equal(rbfForeignOutput.ScriptPubKey, rbf2ForeignOutput.ScriptPubKey);

			Assert.True(rbf2.Fee > rbf.Fee);

			Assert.False(rbf2.SpendsUnconfirmed);

			Assert.Empty(rbf2.Transaction.ForeignInputs);

			Assert.True(rbf2.Transaction.IsReplacement);
			Assert.False(rbf2.Transaction.IsCPFP);
			Assert.False(rbf2.Transaction.IsCPFPd);
			Assert.Empty(rbf2.Transaction.ParentsThisTxPaysFor);
			Assert.Empty(rbf2.Transaction.ChildrenPayForThisTx);
			Assert.True(rbf2.Transaction.IsSpeedup);
			Assert.False(rbf2.Transaction.IsCancellation);

			Assert.Equal("bar", rbf2.Transaction.Labels);
			Assert.Empty(rbf2.Transaction.WalletOutputs.SelectMany(x => x.HdPubKey.Labels));

			await rpc.GenerateAsync(1);
			waitCount = 0;
			while (wallet.Coins.Any(x => !x.Confirmed))
			{
				await Task.Delay(1000);
				waitCount++;
				if (waitCount >= 21)
				{
					throw new InvalidOperationException($"Wallet didn't recognize transaction confirmation.");
				}
			}

			#endregion CanDoTwiceHasChange

			#region HasNoChange

			rpcAddress = await rpc.GetNewAddressAsync();
			txToSpeedUp = wallet.BuildTransaction(password, new PaymentIntent(rpcAddress, MoneyRequest.CreateAllRemaining(), label: "bar"), FeeStrategy.SevenDaysConfirmationTargetStrategy, allowUnconfirmed: true);
			await broadcaster.SendTransactionAsync(txToSpeedUp.Transaction);

			Assert.Equal(Assert.Single(txToSpeedUp.SpentCoins).SpenderTransaction, txToSpeedUp.Transaction);

			rbf = wallet.SpeedUpTransaction(txToSpeedUp.Transaction);

			// Spender is not updated until broadcast:
			Assert.Equal(Assert.Single(txToSpeedUp.SpentCoins).SpenderTransaction, txToSpeedUp.Transaction);
			Assert.Equal(Assert.Single(rbf.SpentCoins).SpenderTransaction, txToSpeedUp.Transaction);

			await broadcaster.SendTransactionAsync(rbf.Transaction);

			Assert.False(wallet.BitcoinStore.TransactionStore.TryGetTransaction(txToSpeedUp.Transaction.GetHash(), out _));

			Assert.Equal(Assert.Single(txToSpeedUp.SpentCoins), Assert.Single(rbf.SpentCoins));
			Assert.Equal(Assert.Single(txToSpeedUp.SpentCoins).SpenderTransaction, rbf.Transaction);
			Assert.Equal(Assert.Single(rbf.SpentCoins).SpenderTransaction, rbf.Transaction);

			var txToSpeedUpOutput = Assert.Single(txToSpeedUp.Transaction.Transaction.Outputs);
			var rbfOutput = Assert.Single(rbf.Transaction.Transaction.Outputs);
			Assert.Equal(txToSpeedUpOutput.ScriptPubKey, rbfOutput.ScriptPubKey);
			Assert.True(txToSpeedUpOutput.Value > rbfOutput.Value);

			Assert.True(rbf.Fee > txToSpeedUp.Fee);

			Assert.False(txToSpeedUp.SpendsUnconfirmed);
			Assert.False(rbf.SpendsUnconfirmed);

			Assert.Empty(txToSpeedUp.Transaction.ForeignInputs);
			Assert.Empty(rbf.Transaction.ForeignInputs);

			Assert.False(txToSpeedUp.Transaction.IsReplacement);
			Assert.False(txToSpeedUp.Transaction.IsCPFP);
			Assert.False(txToSpeedUp.Transaction.IsCPFPd);
			Assert.Empty(txToSpeedUp.Transaction.ParentsThisTxPaysFor);
			Assert.Empty(txToSpeedUp.Transaction.ChildrenPayForThisTx);
			Assert.False(txToSpeedUp.Transaction.IsSpeedup);
			Assert.False(txToSpeedUp.Transaction.IsCancellation);
			Assert.True(rbf.Transaction.IsReplacement);
			Assert.False(rbf.Transaction.IsCPFP);
			Assert.False(rbf.Transaction.IsCPFPd);
			Assert.Empty(rbf.Transaction.ParentsThisTxPaysFor);
			Assert.Empty(rbf.Transaction.ChildrenPayForThisTx);
			Assert.True(rbf.Transaction.IsSpeedup);
			Assert.False(rbf.Transaction.IsCancellation);

			Assert.Equal("bar", txToSpeedUp.Transaction.Labels);
			Assert.Equal("bar", rbf.Transaction.Labels);
			Assert.Empty(txToSpeedUp.Transaction.WalletOutputs.SelectMany(x => x.HdPubKey.Labels));
			Assert.Empty(rbf.Transaction.WalletOutputs.SelectMany(x => x.HdPubKey.Labels));

			#endregion HasNoChange

			#region CanDoTwiceHasNoChange

			rbf2 = wallet.SpeedUpTransaction(rbf.Transaction);

			// Spender is not updated until broadcast:
			Assert.Equal(Assert.Single(rbf.SpentCoins).SpenderTransaction, rbf.Transaction);
			Assert.Equal(Assert.Single(rbf2.SpentCoins).SpenderTransaction, rbf.Transaction);

			await broadcaster.SendTransactionAsync(rbf2.Transaction);

			Assert.False(wallet.BitcoinStore.TransactionStore.TryGetTransaction(rbf.Transaction.GetHash(), out _));

			Assert.Equal(Assert.Single(rbf.SpentCoins), Assert.Single(rbf2.SpentCoins));
			Assert.Equal(Assert.Single(rbf.SpentCoins).SpenderTransaction, rbf2.Transaction);
			Assert.Equal(Assert.Single(rbf2.SpentCoins).SpenderTransaction, rbf2.Transaction);

			var rbf2Output = Assert.Single(rbf2.Transaction.Transaction.Outputs);
			Assert.Equal(rbfOutput.ScriptPubKey, rbf2Output.ScriptPubKey);
			Assert.True(rbfOutput.Value > rbf2Output.Value);

			Assert.True(rbf2.Fee > rbf.Fee);

			Assert.False(rbf2.SpendsUnconfirmed);

			Assert.Empty(rbf2.Transaction.ForeignInputs);

			Assert.True(rbf2.Transaction.IsReplacement);
			Assert.False(rbf2.Transaction.IsCPFP);
			Assert.False(rbf2.Transaction.IsCPFPd);
			Assert.Empty(rbf2.Transaction.ParentsThisTxPaysFor);
			Assert.Empty(rbf2.Transaction.ChildrenPayForThisTx);
			Assert.True(rbf2.Transaction.IsSpeedup);
			Assert.False(rbf2.Transaction.IsCancellation);

			await rpc.GenerateAsync(1);
			waitCount = 0;
			while (wallet.Coins.Any(x => !x.Confirmed))
			{
				await Task.Delay(1000);
				waitCount++;
				if (waitCount >= 21)
				{
					throw new InvalidOperationException($"Wallet didn't recognize transaction confirmation.");
				}
			}

			Assert.Equal("bar", rbf2.Transaction.Labels);
			Assert.Empty(rbf2.Transaction.WalletOutputs.SelectMany(x => x.HdPubKey.Labels));

			#endregion CanDoTwiceHasNoChange

			#region TooSmallHasNoChange

			var fundingTxId = await rpc.SendToAddressAsync(keyManager.GetNextReceiveKey("foo").GetP2wpkhAddress(network), Money.Satoshis(30_000));
			Assert.NotNull(txId);
			await rpc.GenerateAsync(1);
			SmartTransaction? fundingTx = null;
			waitCount = 0;
			while (!wallet.BitcoinStore.TransactionStore.TryGetTransaction(fundingTxId, out fundingTx) || fundingTx?.Confirmed is false)
			{
				await Task.Delay(1000);
				waitCount++;
				if (waitCount >= 21)
				{
					throw new InvalidOperationException($"Wallet didn't recognize transaction confirmation.");
				}
			}

			txToSpeedUp = wallet.BuildTransaction(password, new PaymentIntent(rpcAddress, MoneyRequest.CreateAllRemaining(), label: "bar"), FeeStrategy.CreateFromFeeRate(10), allowedInputs: fundingTx!.GetWalletOutputs(keyManager).Select(x => x.Outpoint));
			await broadcaster.SendTransactionAsync(txToSpeedUp.Transaction);

			Assert.Throws<TransactionFeeOverpaymentException>(() => wallet.SpeedUpTransaction(txToSpeedUp.Transaction));

			#endregion TooSmallHasNoChange

			#region TooSmallToRbfButCanCpfpHasChange

			Assert.Empty(wallet.Coins);

			// The helper coin is to pick up when we realize the change is too small to RBF.
			var helperCoinAmount = Money.Coins(0.1m);
			var activeAmount = Money.Coins(1);
			var changeAmount = Money.Satoshis(20_000);

			fundingTxId = await rpc.SendToAddressAsync(keyManager.GetNextReceiveKey("foo").GetP2wpkhAddress(network), activeAmount);
			var helperCoinTxId = await rpc.SendToAddressAsync(keyManager.GetNextReceiveKey("bar").GetP2wpkhAddress(network), helperCoinAmount);
			Assert.NotNull(txId);
			await rpc.GenerateAsync(1);
			fundingTx = null;
			SmartTransaction? helperCoinTx = null;
			waitCount = 0;
			while ((!wallet.BitcoinStore.TransactionStore.TryGetTransaction(fundingTxId, out fundingTx) || fundingTx?.Confirmed is false)
				|| (!wallet.BitcoinStore.TransactionStore.TryGetTransaction(helperCoinTxId, out helperCoinTx) || helperCoinTx?.Confirmed is false))
			{
				await Task.Delay(1000);
				waitCount++;
				if (waitCount >= 21)
				{
					throw new InvalidOperationException($"Wallet didn't recognize transaction confirmation.");
				}
			}

			txToSpeedUp = wallet.BuildTransaction(password, new PaymentIntent(rpcAddress, MoneyRequest.Create(activeAmount - changeAmount, subtractFee: true), label: "bar"), FeeStrategy.CreateFromFeeRate(10), allowedInputs: fundingTx!.GetWalletOutputs(keyManager).Select(x => x.Outpoint));
			await broadcaster.SendTransactionAsync(txToSpeedUp.Transaction);

			// Make sure we can spend the coins together by giving them a high anonset.
			foreach (var c in wallet.Coins)
			{
				c.HdPubKey.SetAnonymitySet(9_000);
				c.IsSufficientlyDistancedFromExternalKeys = true;
			}

			var cpfp = wallet.SpeedUpTransaction(txToSpeedUp.Transaction);

			Assert.False(txToSpeedUp.Transaction.IsCPFP);
			Assert.False(txToSpeedUp.Transaction.IsCPFPd); // Should be true, but it is not broadcasted yet.
			Assert.Empty(txToSpeedUp.Transaction.ParentsThisTxPaysFor);
			Assert.Empty(txToSpeedUp.Transaction.ChildrenPayForThisTx); // Should be single, but it is not broadcasted yet.

			Assert.False(cpfp.Transaction.IsReplacement);
			Assert.True(cpfp.Transaction.IsCPFP);
			Assert.False(cpfp.Transaction.IsCPFPd);
			Assert.Equal(txToSpeedUp.Transaction.GetHash(), Assert.Single(cpfp.Transaction.ParentsThisTxPaysFor).GetHash());
			Assert.Empty(cpfp.Transaction.ChildrenPayForThisTx);
			Assert.Equal(2, cpfp.SpentCoins.Count());
			Assert.Empty(cpfp.Transaction.ForeignInputs);
			Assert.Single(cpfp.Transaction.Transaction.Outputs);
			Assert.Contains(Assert.Single(txToSpeedUp.InnerWalletOutputs), cpfp.SpentCoins);

			Assert.Equal("bar", txToSpeedUp.Transaction.Labels);
			Assert.Empty(cpfp.Transaction.Labels);
			Assert.Empty(txToSpeedUp.Transaction.WalletOutputs.SelectMany(x => x.HdPubKey.Labels));
			Assert.Empty(cpfp.Transaction.WalletOutputs.SelectMany(x => x.HdPubKey.Labels));

			#endregion TooSmallToRbfButCanCpfpHasChange

			#region TooSmallHasChange

			// In this case we'd pay more fee than the active output's value, which makes no sense to bring in another coin.
			helperCoinAmount = Money.Coins(0.1m);
			activeAmount = Money.Satoshis(40_000);
			changeAmount = Money.Satoshis(20_000);

			fundingTxId = await rpc.SendToAddressAsync(keyManager.GetNextReceiveKey("foo").GetP2wpkhAddress(network), activeAmount);
			helperCoinTxId = await rpc.SendToAddressAsync(keyManager.GetNextReceiveKey("bar").GetP2wpkhAddress(network), helperCoinAmount);
			Assert.NotNull(txId);
			await rpc.GenerateAsync(1);
			fundingTx = null;
			helperCoinTx = null;
			waitCount = 0;
			while ((!wallet.BitcoinStore.TransactionStore.TryGetTransaction(fundingTxId, out fundingTx) || fundingTx?.Confirmed is false)
				|| (!wallet.BitcoinStore.TransactionStore.TryGetTransaction(helperCoinTxId, out helperCoinTx) || helperCoinTx?.Confirmed is false))
			{
				await Task.Delay(1000);
				waitCount++;
				if (waitCount >= 21)
				{
					throw new InvalidOperationException($"Wallet didn't recognize transaction confirmation.");
				}
			}

			txToSpeedUp = wallet.BuildTransaction(password, new PaymentIntent(rpcAddress, MoneyRequest.Create(activeAmount - changeAmount, subtractFee: true), label: "bar"), FeeStrategy.CreateFromFeeRate(10), allowedInputs: fundingTx!.GetWalletOutputs(keyManager).Select(x => x.Outpoint));
			await broadcaster.SendTransactionAsync(txToSpeedUp.Transaction);

			Assert.Throws<TransactionFeeOverpaymentException>(() => wallet.SpeedUpTransaction(txToSpeedUp.Transaction));

			#endregion TooSmallHasChange

			#region MarnixFoundBug

			// https://github.com/zkSNACKs/WalletWasabi/pull/10976#pullrequestreview-1542077218
			// I speed up a tx which has change, but the additional fee was deducted from the original send amount not from the change utxo?
			// https://mempool.space/testnet/tx/b0de46a002e6487dac3a3a98841380d4ced7e4e8482217b2758dd53df0760af8
			// I sent the 0.0004

			// 1. Let's empty the wallet first.
			var tx = wallet.BuildTransaction(password, new PaymentIntent(await rpc.GetNewAddressAsync(), MoneyRequest.CreateAllRemaining()), FeeStrategy.TwentyMinutesConfirmationTargetStrategy, allowUnconfirmed: true);
			await broadcaster.SendTransactionAsync(tx.Transaction);
			await rpc.GenerateAsync(1);
			waitCount = 0;
			while (wallet.Coins.Any())
			{
				await Task.Delay(1000);
				waitCount++;
				if (waitCount >= 21)
				{
					throw new InvalidOperationException($"Wallet didn't recognize transaction confirmation.");
				}
			}

			// 2. Let's get the exact 9 outputs into our wallet that Marnix had.
			var amounts = new[]
			{
				0.00005000m,
				0.00004900m,
				0.00004490m,
				0.00005000m,
				0.00005000m,
				0.00005000m,
				0.00005000m,
				0.00005000m,
				0.00005000m
			};

			foreach (var amount in amounts)
			{
				await rpc.SendToAddressAsync(keyManager.GetNextReceiveKey("foo").GetP2wpkhAddress(network), Money.Coins(amount));
			}

			await rpc.GenerateAsync(1);
			waitCount = 0;
			while (wallet.Coins.Count() != 9)
			{
				await Task.Delay(1000);
				waitCount++;
				if (waitCount >= 21)
				{
					throw new InvalidOperationException($"Wallet didn't recognize transaction confirmation.");
				}
			}

			// 3. Let's simulate the original tx.
			txToSpeedUp = wallet.BuildTransaction(password, new PaymentIntent(await rpc.GetNewAddressAsync(), MoneyRequest.Create(Money.Coins(0.0004m), subtractFee: false), label: "foo"), FeeStrategy.CreateFromFeeRate(1), allowUnconfirmed: true);

			Assert.Equal(9, txToSpeedUp.SpentCoins.Count());
			Assert.Equal(Money.Coins(0.0004m), txToSpeedUp.OuterWalletOutputs.Single().Amount);

			await broadcaster.SendTransactionAsync(txToSpeedUp.Transaction);

			// 4. Finally let's speed up the tx.
			var feeRate = new FeeRate(2.01m);
			rbf = wallet.SpeedUpTransaction(txToSpeedUp.Transaction, feeRate);
			await broadcaster.SendTransactionAsync(rbf.Transaction);

			Assert.Single(rbf.InnerWalletOutputs);
			Assert.Equal(Money.Coins(0.0004m), Assert.Single(rbf.OuterWalletOutputs).Amount);

			var rbfFeeRate = rbf.Fee.Satoshi / rbf.Transaction.Transaction.GetVirtualSize();
			Assert.True(rbf.Fee > txToSpeedUp.Fee);
			Assert.Equal(2, rbfFeeRate);

			Assert.True(rbf.Transaction.IsSpeedup);
			Assert.True(rbf.Transaction.IsReplacement);
			Assert.False(rbf.Transaction.IsCPFP);
			Assert.False(rbf.Transaction.IsCPFPd);
			Assert.False(rbf.Transaction.IsCancellation);

			Assert.Equal("foo", txToSpeedUp.Transaction.Labels);
			Assert.Equal("foo", rbf.Transaction.Labels);
			Assert.Empty(txToSpeedUp.Transaction.WalletOutputs.SelectMany(x => x.HdPubKey.Labels));
			Assert.Empty(rbf.Transaction.WalletOutputs.SelectMany(x => x.HdPubKey.Labels));

			#endregion MarnixFoundBug
		}
		finally
		{
			bitcoinStore.IndexStore.NewFilters -= setup.Wallet_NewFiltersProcessed;
			await walletManager.RemoveAndStopAllAsync(CancellationToken.None);
			await synchronizer.StopAsync(CancellationToken.None);
			await feeProvider.StopAsync(CancellationToken.None);
			nodes?.Dispose();
			node?.Disconnect();
		}
	}
}

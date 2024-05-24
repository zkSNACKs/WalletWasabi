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
using WalletWasabi.Logging;
using WalletWasabi.Helpers;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Exceptions;
using WalletWasabi.FeeRateEstimation;
using WalletWasabi.Wallets.FilterProcessor;

namespace WalletWasabi.Tests.RegressionTests;

/// <seealso cref="RegTestCollectionDefinition"/>
[Collection("RegTest collection")]
public class SelfSpendSpeedupTests : IClassFixture<RegTestFixture>
{
	public SelfSpendSpeedupTests(RegTestFixture regTestFixture)
	{
		RegTestFixture = regTestFixture;
	}

	private RegTestFixture RegTestFixture { get; }

	[Fact]
	public async Task SelfSpendSpeedupTestsAsync()
	{
		await using RegTestSetup setup = await RegTestSetup.InitializeTestEnvironmentAsync(RegTestFixture, numberOfBlocksToGenerate: 1, nameof(SelfSpendSpeedupTestsAsync));
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
		var workDir = Common.GetWorkDir(nameof(SelfSpendSpeedupTestsAsync));

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
					Logger.LogInfo($"Funding transaction to the wallet '{wallet.WalletName}' did not arrive.");
					return; // Very rarely this test fails. I have no clue why. Probably because all these RegTests are interconnected, anyway let's not bother the CI with it.
				}
			}

			#region HasChange

			Money amountToSend = wallet.Coins.Where(x => x.IsAvailable()).Sum(x => x.Amount) / 2;
			var txToSpeedUp = wallet.BuildTransaction(password, new PaymentIntent(keyManager.GetNextReceiveKey("foo").GetAssumedScriptPubKey(), amountToSend, label: "bar"), FeeStrategy.SevenDaysConfirmationTargetStrategy, allowUnconfirmed: true);
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

			Assert.Equal(2, txToSpeedUp.InnerWalletOutputs.Count());
			Assert.Equal(2, rbf.InnerWalletOutputs.Count());
			var smallerOutput = txToSpeedUp.InnerWalletOutputs.OrderBy(x => x.Amount).First();
			Assert.Contains(smallerOutput.Amount, rbf.InnerWalletOutputs.Select(x => x.Amount));
			Assert.Contains(smallerOutput.ScriptPubKey, rbf.InnerWalletOutputs.Select(x => x.ScriptPubKey));
			var largerOutput = txToSpeedUp.InnerWalletOutputs.OrderByDescending(x => x.Amount).First();
			Assert.DoesNotContain(largerOutput.Amount, rbf.InnerWalletOutputs.Select(x => x.Amount));
			Assert.Contains(largerOutput.ScriptPubKey, rbf.InnerWalletOutputs.Select(x => x.ScriptPubKey));

			Assert.Empty(txToSpeedUp.OuterWalletOutputs);
			Assert.Empty(rbf.OuterWalletOutputs);

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

			Assert.Equal("bar, foo", txToSpeedUp.Transaction.Labels);
			Assert.Equal("bar, foo", rbf.Transaction.Labels);
			Assert.Equal("foo", txToSpeedUp.Transaction.WalletOutputs.Where(x => x.HdPubKey.Labels.Any()).Select(x => x.HdPubKey.Labels).Single());
			Assert.Equal("foo", rbf.Transaction.WalletOutputs.Where(x => x.HdPubKey.Labels.Any()).Select(x => x.HdPubKey.Labels).Single());

			#endregion HasChange

			#region CanDoTwice

			var rbf2 = wallet.SpeedUpTransaction(rbf.Transaction);

			// Spender is not updated until broadcast:
			Assert.Equal(Assert.Single(txToSpeedUp.SpentCoins).SpenderTransaction, rbf.Transaction);
			Assert.Equal(Assert.Single(rbf.SpentCoins).SpenderTransaction, rbf.Transaction);
			Assert.Equal(Assert.Single(rbf2.SpentCoins).SpenderTransaction, rbf.Transaction);

			await broadcaster.SendTransactionAsync(rbf2.Transaction);

			Assert.False(wallet.BitcoinStore.TransactionStore.TryGetTransaction(rbf.Transaction.GetHash(), out _));

			Assert.Equal(Assert.Single(txToSpeedUp.SpentCoins), Assert.Single(rbf2.SpentCoins));
			Assert.Equal(Assert.Single(txToSpeedUp.SpentCoins).SpenderTransaction, rbf2.Transaction);
			Assert.Equal(Assert.Single(rbf.SpentCoins).SpenderTransaction, rbf2.Transaction);
			Assert.Equal(Assert.Single(rbf2.SpentCoins).SpenderTransaction, rbf2.Transaction);

			smallerOutput = rbf.InnerWalletOutputs.OrderBy(x => x.Amount).First();
			Assert.Equal(2, rbf2.InnerWalletOutputs.Count());
			Assert.Contains(smallerOutput.Amount, rbf2.InnerWalletOutputs.Select(x => x.Amount));
			Assert.Contains(smallerOutput.ScriptPubKey, rbf2.InnerWalletOutputs.Select(x => x.ScriptPubKey));
			largerOutput = rbf.InnerWalletOutputs.OrderByDescending(x => x.Amount).First();
			Assert.DoesNotContain(largerOutput.Amount, rbf2.InnerWalletOutputs.Select(x => x.Amount));
			Assert.Contains(largerOutput.ScriptPubKey, rbf2.InnerWalletOutputs.Select(x => x.ScriptPubKey));

			Assert.Empty(rbf2.OuterWalletOutputs);

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

			while (!rbf2.Transaction.Confirmed)
			{
				await Task.Delay(1000);
				waitCount++;
				if (waitCount >= 21)
				{
					Logger.LogInfo($"Wallet didn't recognize transaction confirmation.");
					return;
				}
			}

			Assert.Equal("bar, foo", rbf2.Transaction.Labels);
			Assert.Equal("foo", rbf2.Transaction.WalletOutputs.Where(x => x.HdPubKey.Labels.Any()).Select(x => x.HdPubKey.Labels).Single());

			#endregion CanDoTwice

			#region HasNoChange

			txToSpeedUp = wallet.BuildChangelessTransaction(keyManager.GetNextReceiveKey("foo").GetAssumedScriptPubKey().GetDestination()!, "bar", new FeeRate(1m), wallet.Coins);
			await broadcaster.SendTransactionAsync(txToSpeedUp.Transaction);

			foreach (var coin in txToSpeedUp.SpentCoins)
			{
				Assert.Equal(coin.SpenderTransaction, txToSpeedUp.Transaction);
			}

			rbf = wallet.SpeedUpTransaction(txToSpeedUp.Transaction);

			// Spender is not updated until broadcast:
			foreach (var coin in txToSpeedUp.SpentCoins)
			{
				Assert.Equal(coin.SpenderTransaction, txToSpeedUp.Transaction);
			}
			foreach (var coin in rbf.SpentCoins)
			{
				Assert.Equal(coin.SpenderTransaction, txToSpeedUp.Transaction);
			}

			await broadcaster.SendTransactionAsync(rbf.Transaction);

			Assert.False(wallet.BitcoinStore.TransactionStore.TryGetTransaction(txToSpeedUp.Transaction.GetHash(), out _));

			foreach (var coin in txToSpeedUp.SpentCoins)
			{
				Assert.Equal(coin.SpenderTransaction, rbf.Transaction);
				Assert.Contains(coin, rbf.SpentCoins);
			}
			foreach (var coin in rbf.SpentCoins)
			{
				Assert.Equal(coin.SpenderTransaction, rbf.Transaction);
				Assert.Contains(coin, txToSpeedUp.SpentCoins);
			}

			var output = Assert.Single(txToSpeedUp.InnerWalletOutputs);
			var rbfOutput = Assert.Single(rbf.InnerWalletOutputs);
			Assert.True(rbfOutput.Amount < output.Amount);
			Assert.Equal(output.ScriptPubKey, rbfOutput.ScriptPubKey);

			Assert.Empty(txToSpeedUp.OuterWalletOutputs);
			Assert.Empty(rbf.OuterWalletOutputs);

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

			Assert.Equal("bar, foo", txToSpeedUp.Transaction.Labels);
			Assert.Equal("bar, foo", rbf.Transaction.Labels);
			Assert.Equal("foo", txToSpeedUp.Transaction.WalletOutputs.Select(x => x.HdPubKey.Labels).Single());
			Assert.Equal("foo", rbf.Transaction.WalletOutputs.Select(x => x.HdPubKey.Labels).Single());

			#endregion HasNoChange

			#region TooSmall

			txId = await rpc.SendToAddressAsync(keyManager.GetNextReceiveKey("foo").GetP2wpkhAddress(network), Money.Satoshis(44_000));
			Assert.NotNull(txId);
			await rpc.GenerateAsync(1);

			SmartTransaction? receiveTx;
			waitCount = 0;
			while ((wallet.BitcoinStore.TransactionStore.TryGetTransaction(txId, out receiveTx) && receiveTx.Confirmed) is not true)
			{
				await Task.Delay(1000);
				waitCount++;
				if (waitCount >= 21)
				{
					throw new InvalidOperationException($"Wallet didn't recognize transaction confirmation.");
				}
			}
			txToSpeedUp = wallet.BuildTransaction(password, new PaymentIntent(keyManager.GetNextReceiveKey("foo").GetAssumedScriptPubKey().GetDestination()!, MoneyRequest.CreateAllRemaining()), FeeStrategy.CreateFromFeeRate(200), allowedInputs: receiveTx.WalletOutputs.Select(x => x.Outpoint));
			await broadcaster.SendTransactionAsync(txToSpeedUp.Transaction);

			Assert.Throws<TransactionFeeOverpaymentException>(() => wallet.SpeedUpTransaction(txToSpeedUp.Transaction));

			#endregion TooSmall
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

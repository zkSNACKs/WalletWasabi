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
using WalletWasabi.FeeRateEstimation;
using WalletWasabi.Models;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using WalletWasabi.Tests.XunitConfiguration;
using WalletWasabi.Wallets;
using WalletWasabi.WebClients.Wasabi;
using Xunit;
using WalletWasabi.Helpers;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.Wallets.FilterProcessor;

namespace WalletWasabi.Tests.RegressionTests;

/// <seealso cref="RegTestCollectionDefinition"/>
[Collection("RegTest collection")]
public class CancelTests : IClassFixture<RegTestFixture>
{
	public CancelTests(RegTestFixture regTestFixture)
	{
		RegTestFixture = regTestFixture;
	}

	private RegTestFixture RegTestFixture { get; }

	[Fact]
	public async Task CancelTestsAsync()
	{
		await using RegTestSetup setup = await RegTestSetup.InitializeTestEnvironmentAsync(RegTestFixture, numberOfBlocksToGenerate: 1, nameof(CancelTestsAsync));
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
		var workDir = Common.GetWorkDir(nameof(CancelTestsAsync));

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
			var externalAddr = await rpc.GetNewAddressAsync(CancellationToken.None);
			var txToCancel = wallet.BuildTransaction(password, new PaymentIntent(externalAddr, amountToSend, label: "bar"), FeeStrategy.SevenDaysConfirmationTargetStrategy, allowUnconfirmed: true);

			SetHighInputAnonsets(txToCancel);

			await broadcaster.SendTransactionAsync(txToCancel.Transaction);

			AssertAllAnonsets1(txToCancel);

			Assert.Equal("bar", txToCancel.Transaction.Labels.Single());
			foreach (var op in txToCancel.InnerWalletOutputs)
			{
				Assert.Empty(op.HdPubKey.Labels);
			}

			var cancellingTx = wallet.CancelTransaction(txToCancel.Transaction);

			var changeOut = Assert.Single(txToCancel.InnerWalletOutputs);
			var cancelOut = Assert.Single(cancellingTx.InnerWalletOutputs);
			Assert.Equal(changeOut.ScriptPubKey, cancelOut.ScriptPubKey);

			Assert.Single(txToCancel.OuterWalletOutputs);
			Assert.Empty(cancellingTx.OuterWalletOutputs);

			Assert.True(txToCancel.Fee < cancellingTx.Fee);

			Assert.Equal(txToCancel.SpentCoins, cancellingTx.SpentCoins);

			await broadcaster.SendTransactionAsync(cancellingTx.Transaction);

			Assert.False(wallet.BitcoinStore.TransactionStore.TryGetTransaction(txToCancel.Transaction.GetHash(), out _));

			AssertAllAnonsets1(cancellingTx);

			Assert.False(txToCancel.Transaction.IsReplacement);
			Assert.False(txToCancel.Transaction.IsCPFP);
			Assert.False(txToCancel.Transaction.IsCPFPd);
			Assert.Empty(txToCancel.Transaction.ParentsThisTxPaysFor);
			Assert.Empty(txToCancel.Transaction.ChildrenPayForThisTx);
			Assert.False(txToCancel.Transaction.IsSpeedup);
			Assert.False(txToCancel.Transaction.IsCancellation);
			Assert.True(cancellingTx.Transaction.IsReplacement);
			Assert.False(cancellingTx.Transaction.IsCPFP);
			Assert.False(cancellingTx.Transaction.IsCPFPd);
			Assert.Empty(cancellingTx.Transaction.ParentsThisTxPaysFor);
			Assert.Empty(cancellingTx.Transaction.ChildrenPayForThisTx);
			Assert.False(cancellingTx.Transaction.IsSpeedup);
			Assert.True(cancellingTx.Transaction.IsCancellation);

			Assert.Equal("bar", cancellingTx.Transaction.Labels.Single());
			foreach (var op in cancellingTx.InnerWalletOutputs)
			{
				Assert.Empty(op.HdPubKey.Labels);
			}

			await rpc.GenerateAsync(1);

			#endregion HasChange

			#region NoChange

			externalAddr = await rpc.GetNewAddressAsync(CancellationToken.None);
			txToCancel = wallet.BuildChangelessTransaction(externalAddr, "foo", new FeeRate(1m), cancellingTx.InnerWalletOutputs);

			SetHighInputAnonsets(txToCancel);

			await broadcaster.SendTransactionAsync(txToCancel.Transaction);

			AssertAllAnonsets1(txToCancel);

			Assert.Equal("foo", txToCancel.Transaction.Labels.Single());
			foreach (var op in txToCancel.InnerWalletOutputs)
			{
				Assert.Empty(op.HdPubKey.Labels);
			}

			cancellingTx = wallet.CancelTransaction(txToCancel.Transaction);

			Assert.Empty(txToCancel.InnerWalletOutputs);
			Assert.Single(cancellingTx.InnerWalletOutputs);

			Assert.Single(txToCancel.OuterWalletOutputs);
			Assert.Empty(cancellingTx.OuterWalletOutputs);

			Assert.True(txToCancel.Fee < cancellingTx.Fee);

			Assert.Equal(txToCancel.SpentCoins, cancellingTx.SpentCoins);

			await broadcaster.SendTransactionAsync(cancellingTx.Transaction);

			Assert.False(wallet.BitcoinStore.TransactionStore.TryGetTransaction(txToCancel.Transaction.GetHash(), out _));

			AssertAllAnonsets1(cancellingTx);

			Assert.False(txToCancel.Transaction.IsReplacement);
			Assert.False(txToCancel.Transaction.IsCPFP);
			Assert.False(txToCancel.Transaction.IsCPFPd);
			Assert.Empty(txToCancel.Transaction.ParentsThisTxPaysFor);
			Assert.Empty(txToCancel.Transaction.ChildrenPayForThisTx);
			Assert.False(txToCancel.Transaction.IsCancellation);
			Assert.True(cancellingTx.Transaction.IsReplacement);
			Assert.False(cancellingTx.Transaction.IsCPFP);
			Assert.False(cancellingTx.Transaction.IsCPFPd);
			Assert.Empty(cancellingTx.Transaction.ParentsThisTxPaysFor);
			Assert.Empty(cancellingTx.Transaction.ChildrenPayForThisTx);
			Assert.True(cancellingTx.Transaction.IsCancellation);

			Assert.Equal("foo", cancellingTx.Transaction.Labels.Single());
			foreach (var op in cancellingTx.InnerWalletOutputs)
			{
				Assert.Empty(op.HdPubKey.Labels);
			}

			#endregion NoChange

			#region CantCancel

			// Can't cancel cancelled transaction.
			Assert.Throws<InvalidOperationException>(() => wallet.CancelTransaction(cancellingTx.Transaction));

			// Can't cancel cancellation transaction.
			amountToSend = wallet.Coins.Where(x => x.IsAvailable()).Sum(x => x.Amount) / 2;
			externalAddr = await rpc.GetNewAddressAsync(CancellationToken.None);
			txToCancel = wallet.BuildTransaction(password, new PaymentIntent(externalAddr, amountToSend, label: "bar"), FeeStrategy.SevenDaysConfirmationTargetStrategy, allowUnconfirmed: true);
			await broadcaster.SendTransactionAsync(txToCancel.Transaction);
			await rpc.GenerateAsync(1);

			waitCount = 0;
			while (!txToCancel.Transaction.Confirmed)
			{
				await Task.Delay(1000);
				waitCount++;
				if (waitCount >= 21)
				{
					throw new InvalidOperationException($"Wallet didn't recognize transaction confirmation.");
				}
			}

			Assert.Throws<InvalidOperationException>(() => wallet.CancelTransaction(txToCancel.Transaction));

			// Nonsense to cancel self spend.
			txToCancel = wallet.BuildChangelessTransaction(wallet.GetNextReceiveAddress(new[] { "foo " }).GetAssumedScriptPubKey().GetDestination()!, "foo", new FeeRate(1m), wallet.Coins.Select(x => x.Outpoint));
			await broadcaster.SendTransactionAsync(txToCancel.Transaction);

			Assert.Equal("Transaction is not cancellable.", Assert.Throws<InvalidOperationException>(() => wallet.CancelTransaction(txToCancel.Transaction)).Message);
			await rpc.GenerateAsync(1);

			// Dangerous to cancel if an output is spent.
			amountToSend = wallet.Coins.Where(x => x.IsAvailable()).Sum(x => x.Amount) / 2;
			externalAddr = await rpc.GetNewAddressAsync(CancellationToken.None);
			txToCancel = wallet.BuildTransaction(password, new PaymentIntent(externalAddr, amountToSend, label: "bar"), FeeStrategy.SevenDaysConfirmationTargetStrategy, allowUnconfirmed: true);
			await broadcaster.SendTransactionAsync(txToCancel.Transaction);

			var spendingTxToCancel = wallet.BuildChangelessTransaction(await rpc.GetNewAddressAsync(CancellationToken.None), "foo", new FeeRate(1m), txToCancel.InnerWalletOutputs);
			await broadcaster.SendTransactionAsync(spendingTxToCancel.Transaction);

			// Ensure full data integrity regarding recognizing spends.
			var sameCoin1 = txToCancel.InnerWalletOutputs.Single();
			var sameCoin2 = spendingTxToCancel.SpentCoins.Single(x => x == sameCoin1);
			var sameCoin3 = txToCancel.Transaction.WalletOutputs.Single(x => x == sameCoin1);
			var sameCoin4 = wallet.GetAllCoins().Single(x => x == sameCoin1);
			Assert.NotNull(sameCoin1.SpenderTransaction);
			Assert.NotNull(sameCoin2.SpenderTransaction);
			Assert.NotNull(sameCoin3.SpenderTransaction);
			Assert.NotNull(sameCoin4.SpenderTransaction);

			Assert.Equal("foo", spendingTxToCancel.Transaction.Labels.Single());
			foreach (var op in spendingTxToCancel.InnerWalletOutputs)
			{
				Assert.Empty(op.HdPubKey.Labels);
			}

			Assert.Throws<InvalidOperationException>(() => wallet.CancelTransaction(txToCancel.Transaction));
			cancellingTx = wallet.CancelTransaction(spendingTxToCancel.Transaction);
			await broadcaster.SendTransactionAsync(cancellingTx.Transaction);

			Assert.False(wallet.BitcoinStore.TransactionStore.TryGetTransaction(spendingTxToCancel.Transaction.GetHash(), out _));

			Assert.False(spendingTxToCancel.Transaction.IsReplacement);
			Assert.False(spendingTxToCancel.Transaction.IsCPFP);
			Assert.False(spendingTxToCancel.Transaction.IsCPFPd);
			Assert.Empty(spendingTxToCancel.Transaction.ParentsThisTxPaysFor);
			Assert.Empty(spendingTxToCancel.Transaction.ChildrenPayForThisTx);
			Assert.False(spendingTxToCancel.Transaction.IsSpeedup);
			Assert.False(spendingTxToCancel.Transaction.IsCancellation);
			Assert.True(cancellingTx.Transaction.IsReplacement);
			Assert.False(cancellingTx.Transaction.IsCPFP);
			Assert.False(cancellingTx.Transaction.IsCPFPd);
			Assert.Empty(cancellingTx.Transaction.ParentsThisTxPaysFor);
			Assert.Empty(cancellingTx.Transaction.ChildrenPayForThisTx);
			Assert.False(cancellingTx.Transaction.IsSpeedup);
			Assert.True(cancellingTx.Transaction.IsCancellation);

			Assert.Equal("foo", cancellingTx.Transaction.Labels.Single());
			foreach (var op in cancellingTx.InnerWalletOutputs)
			{
				Assert.Empty(op.HdPubKey.Labels);
			}

			#endregion CantCancel
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

	private static void AssertAllAnonsets1(BuildTransactionResult txToCancel)
	{
		foreach (var input in txToCancel.SpentCoins)
		{
			Assert.Equal(1, input.AnonymitySet);
		}
		foreach (var output in txToCancel.InnerWalletOutputs)
		{
			Assert.Equal(1, output.AnonymitySet);
		}
	}

	private static void SetHighInputAnonsets(BuildTransactionResult tx)
	{
		foreach (var input in tx.SpentCoins)
		{
			input.HdPubKey.SetAnonymitySet(999);
		}
	}
}

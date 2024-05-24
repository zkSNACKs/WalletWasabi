using System.Collections.Generic;
using NBitcoin;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend;
using WalletWasabi.Backend.Models;
using WalletWasabi.Backend.Models.Responses;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Blockchain.Mempool;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Helpers;
using WalletWasabi.Models;
using WalletWasabi.Stores;
using WalletWasabi.Tests.XunitConfiguration;
using WalletWasabi.Wallets;

namespace WalletWasabi.Tests.RegressionTests;

public class RegTestSetup : IAsyncDisposable
{
	[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "The variable must be a field unless refactored.")]
	public long FiltersProcessedByWalletCount;

	public RegTestSetup(RegTestFixture regTestFixture, string dir)
	{
		RegTestFixture = regTestFixture;
		ServiceConfiguration = new ServiceConfiguration(regTestFixture.BackendRegTestNode.P2pEndPoint, Money.Coins(Constants.DefaultDustThreshold));

		SmartHeaderChain smartHeaderChain = new();
		IndexStore = new IndexStore(Path.Combine(dir, "indexStore"), Network, smartHeaderChain);
		TransactionStore = new AllTransactionStore(Path.Combine(dir, "transactionStore"), Network);
		MempoolService mempoolService = new();
		FileSystemBlockRepository blocks = new(Path.Combine(dir, "blocks"), Network);
		BitcoinStore = new BitcoinStore(IndexStore, TransactionStore, mempoolService, smartHeaderChain, blocks);
	}

	public RegTestFixture RegTestFixture { get; }
	public IndexStore IndexStore { get; }
	public BitcoinStore BitcoinStore { get; }
	public AllTransactionStore TransactionStore { get; }
	public IRPCClient RpcClient => RegTestFixture.BackendRegTestNode.RpcClient;
	public Network Network => RpcClient.Network;
	public ServiceConfiguration ServiceConfiguration { get; }
	public string Password { get; } = "password";

	public static async Task<RegTestSetup> InitializeTestEnvironmentAsync(
		RegTestFixture regTestFixture,
		int numberOfBlocksToGenerate,
		string path)
	{
		string dir = Helpers.Common.GetWorkDir(path);
		RegTestSetup setup = new(regTestFixture, dir);

		if (numberOfBlocksToGenerate != 0)
		{
			await setup.RpcClient.GenerateAsync(numberOfBlocksToGenerate).ConfigureAwait(false); // Make sure everything is confirmed.
		}

		await setup.BitcoinStore.InitializeAsync().ConfigureAwait(false);

		return setup;
	}

	public async Task WaitForFiltersToBeProcessedAsync(TimeSpan timeout, int numberOfFiltersToWaitFor)
	{
		var times = 0;
		while (Interlocked.Read(ref FiltersProcessedByWalletCount) < numberOfFiltersToWaitFor)
		{
			if (times > timeout.TotalSeconds)
			{
				throw new TimeoutException($"{nameof(Wallet)} test timed out. Filter was not processed. Needed: {numberOfFiltersToWaitFor}, got only: {Interlocked.Read(ref FiltersProcessedByWalletCount)}.");
			}
			await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
			times++;
		}
	}

	public void Wallet_NewFiltersProcessed(object? sender, IEnumerable<FilterModel> filters)
	{
		foreach (var _ in filters)
		{
			Interlocked.Increment(ref FiltersProcessedByWalletCount);
		}
	}

	public async ValueTask DisposeAsync()
	{
		await IndexStore.DisposeAsync().ConfigureAwait(false);
		await TransactionStore.DisposeAsync().ConfigureAwait(false);
	}
}

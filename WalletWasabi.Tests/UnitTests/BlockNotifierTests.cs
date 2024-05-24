using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Helpers;
using Xunit;

namespace WalletWasabi.Tests.UnitTests;

/// <seealso cref="XunitConfiguration.SerialCollectionDefinition"/>
[Collection("Serial unit tests collection")]
public class BlockNotifierTests
{
	[Fact]
	public async Task GenesisBlockOnlyAsync()
	{
		var chain = new ConcurrentChain(Network.RegTest);
		using var notifier = CreateNotifier(chain);
		var blockAwaiter = new EventAwaiter<Block>(
			h => notifier.OnBlock += h,
			h => notifier.OnBlock -= h);
		var reorgAwaiter = new EventAwaiter<uint256>(
			h => notifier.OnReorg += h,
			h => notifier.OnReorg -= h);

		await notifier.StartAsync(CancellationToken.None);

		// No block notifications nor reorg notifications
		await Assert.ThrowsAsync<TimeoutException>(() => blockAwaiter.WaitAsync(TimeSpan.FromSeconds(1)));
		await Assert.ThrowsAsync<TimeoutException>(() => reorgAwaiter.WaitAsync(TimeSpan.FromSeconds(1)));

		Assert.Equal(Network.RegTest.GenesisHash, notifier.BestBlockHash);

		await notifier.StopAsync(CancellationToken.None);
	}

	[Fact]
	public async Task HitGenesisBlockDuringInitializationAsync()
	{
		var chain = new ConcurrentChain(Network.RegTest);
		foreach (var n in Enumerable.Range(0, 3))
		{
			await AddBlockAsync(chain);
		}
		using var notifier = CreateNotifier(chain);
		var blockAwaiter = new EventAwaiter<Block>(
			h => notifier.OnBlock += h,
			h => notifier.OnBlock -= h);
		var reorgAwaiter = new EventAwaiter<uint256>(
			h => notifier.OnReorg += h,
			h => notifier.OnReorg -= h);

		await notifier.StartAsync(CancellationToken.None);

		// No block notifications nor reorg notifications
		await Assert.ThrowsAsync<TimeoutException>(() => blockAwaiter.WaitAsync(TimeSpan.FromSeconds(1)));
		await Assert.ThrowsAsync<TimeoutException>(() => reorgAwaiter.WaitAsync(TimeSpan.FromSeconds(1)));

		Assert.Equal(chain.Tip.HashBlock, notifier.BestBlockHash);

		await notifier.StopAsync(CancellationToken.None);
	}

	[Fact]
	public async Task NotifyBlocksAsync()
	{
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));

		const int BlockCount = 3;
		var chain = new ConcurrentChain(Network.RegTest);
		using var notifier = CreateNotifier(chain);

		var reorgAwaiter = new EventAwaiter<uint256>(
			h => notifier.OnReorg += h,
			h => notifier.OnReorg -= h);

		await notifier.StartAsync(CancellationToken.None);

		// Assert that the blocks come in the right order
		var height = 0;
		string message = string.Empty;

		void OnBlockInv(object? blockNotifier, Block b)
		{
			uint256 h1 = b.GetHash();
			uint256 h2 = chain.GetBlock(height + 1).HashBlock;

			if (h1 != h2)
			{
				message = string.Format("height={0}, [h1] {1} != [h2] {2}", height, h1, h2);
				cts.Cancel();
				return;
			}

			height++;

			if (height == BlockCount)
			{
				cts.Cancel();
			}
		}

		notifier.OnBlock += OnBlockInv;

		foreach (var n in Enumerable.Range(0, BlockCount))
		{
			await AddBlockAsync(chain);
		}

		notifier.TriggerRound();

		// Waits at most 1.5s given CancellationTokenSource definition
		await Task.WhenAny(Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));

		Assert.True(string.IsNullOrEmpty(message), message);

		// Three blocks notifications
		Assert.Equal(chain.Height, height);

		// No reorg notifications
		await Assert.ThrowsAsync<TimeoutException>(() => reorgAwaiter.WaitAsync(TimeSpan.FromSeconds(1)));
		Assert.Equal(chain.Tip.HashBlock, notifier.BestBlockHash);

		notifier.OnBlock -= OnBlockInv;
		await notifier.StopAsync(CancellationToken.None);
	}

	[Fact]
	public async Task SimpleReorgAsync()
	{
		var chain = new ConcurrentChain(Network.RegTest);
		using var notifier = CreateNotifier(chain);

		var blockAwaiter = new EventsAwaiter<Block>(
			h => notifier.OnBlock += h,
			h => notifier.OnBlock -= h,
			5);

		var reorgAwaiter = new EventAwaiter<uint256>(
			h => notifier.OnReorg += h,
			h => notifier.OnReorg -= h);

		await notifier.StartAsync(CancellationToken.None);

		await AddBlockAsync(chain);
		var forkPoint = chain.Tip;
		var blockToBeReorged = await AddBlockAsync(chain);

		chain.SetTip(forkPoint);
		await AddBlockAsync(chain, wait: false);
		await AddBlockAsync(chain, wait: false);
		await AddBlockAsync(chain);
		notifier.TriggerRound();

		// Three blocks notifications
		await blockAwaiter.WaitAsync(TimeSpan.FromSeconds(2));

		// No reorg notifications
		var reorgedkBlock = await reorgAwaiter.WaitAsync(TimeSpan.FromSeconds(1));
		Assert.Equal(blockToBeReorged.HashBlock, reorgedkBlock);
		Assert.Equal(chain.Tip.HashBlock, notifier.BestBlockHash);

		await notifier.StopAsync(CancellationToken.None);
	}

	[Fact]
	public async Task LongChainReorgAsync()
	{
		var chain = new ConcurrentChain(Network.RegTest);
		using var notifier = CreateNotifier(chain);

		var blockAwaiter = new EventsAwaiter<Block>(
			h => notifier.OnBlock += h,
			h => notifier.OnBlock -= h,
			11);

		var reorgAwaiter = new EventsAwaiter<uint256>(
			h => notifier.OnReorg += h,
			h => notifier.OnReorg -= h,
			3);

		await notifier.StartAsync(CancellationToken.None);

		await AddBlockAsync(chain);

		var forkPoint = chain.Tip;
		var firstReorgedChain = new[]
		{
				await AddBlockAsync(chain, wait: false),
				await AddBlockAsync(chain)
			};

		chain.SetTip(forkPoint);
		var secondReorgedChain = new[]
		{
				await AddBlockAsync(chain, wait: false),
				await AddBlockAsync(chain, wait: false),
				await AddBlockAsync(chain)
			};

		chain.SetTip(secondReorgedChain[1]);
		await AddBlockAsync(chain, wait: false);
		await AddBlockAsync(chain, wait: false);
		await AddBlockAsync(chain, wait: false);
		await AddBlockAsync(chain, wait: false);
		await AddBlockAsync(chain);

		// Three blocks notifications
		await blockAwaiter.WaitAsync(TimeSpan.FromSeconds(2));

		// No reorg notifications
		var reorgedkBlock = await reorgAwaiter.WaitAsync(TimeSpan.FromSeconds(1));
		var expectedReorgedBlocks = firstReorgedChain.ToList().Concat(new[] { secondReorgedChain[2] });
		Assert.Subset(reorgedkBlock.ToHashSet(), expectedReorgedBlocks.Select(x => x.Header.GetHash()).ToHashSet());
		Assert.Equal(chain.Tip.HashBlock, notifier.BestBlockHash);

		await notifier.StopAsync(CancellationToken.None);
	}

	[Fact]
	public async Task SuperFastNodeValidationAsync()
	{
		var chain = new ConcurrentChain(Network.RegTest);
		using var notifier = CreateNotifier(chain);
		var blockAwaiter = new EventsAwaiter<Block>(
			h => notifier.OnBlock += h,
			h => notifier.OnBlock -= h,
			144);

		await notifier.StartAsync(CancellationToken.None);

		var lastKnownBlock = await AddBlockAsync(chain);

		foreach (var i in Enumerable.Range(0, 200))
		{
			await AddBlockAsync(chain, wait: false);
		}
		await AddBlockAsync(chain, wait: true);
		notifier.TriggerRound();

		Assert.Equal(chain.Tip.HashBlock, notifier.BestBlockHash);

		var nofifiedBlocks = (await blockAwaiter.WaitAsync(TimeSpan.FromSeconds(1))).ToArray();

		var tip = chain.Tip;
		var pos = nofifiedBlocks.Length - 1;
		while (tip.HashBlock != nofifiedBlocks[pos].GetHash())
		{
			tip = tip.Previous;
		}

		while (pos >= 0)
		{
			Assert.Equal(tip.HashBlock, nofifiedBlocks[pos].GetHash());
			tip = tip.Previous;
			pos--;
		}

		await notifier.StopAsync(CancellationToken.None);
	}

	private BlockNotifier CreateNotifier(ConcurrentChain chain)
	{
		var rpc = new MockRpcClient();
		rpc.OnGetBestBlockHashAsync = () => Task.FromResult(chain.Tip.HashBlock);
		rpc.OnGetBlockAsync = (blockHash) =>
		{
			var block = rpc.Network.Consensus.ConsensusFactory.CreateBlock();
			block.Header = chain.GetBlock(blockHash).Header;
			return Task.FromResult(block);
		};

		rpc.OnGetBlockHeaderAsync = (blockHash) => Task.FromResult(chain.GetBlock(blockHash).Header);

		var notifier = new BlockNotifier(rpc, period: TimeSpan.FromMilliseconds(100));
		return notifier;
	}

	private async Task<ChainedBlock> AddBlockAsync(ConcurrentChain chain, bool wait = true)
	{
		BlockHeader header = Network.RegTest.Consensus.ConsensusFactory.CreateBlockHeader();
		header.Nonce = RandomUtils.GetUInt32();
		header.HashPrevBlock = chain.Tip.HashBlock;
		chain.SetTip(header);
		var block = chain.GetBlock(header.GetHash());
		if (wait)
		{
			await Task.Delay(TimeSpan.FromSeconds(1));
		}
		return block;
	}
}

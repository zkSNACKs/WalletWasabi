using Moq;
using NBitcoin;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Wallets;
using WalletWasabi.Wallets.BlockProvider;
using WalletWasabi.Wallets.FilterProcessor;
using Xunit;
using static WalletWasabi.Wallets.FilterProcessor.BlockDownloadService;

namespace WalletWasabi.Tests.UnitTests.Wallet.FilterProcessor;

/// <summary>
/// Tests for <see cref="BlockDownloadService"/>.
/// </summary>
public class BlockDownloadServiceTests
{
	/// <summary>
	/// Tests <see cref="BlockDownloadService.TryGetBlockAsync(Source, uint256, Priority, uint, CancellationToken)"/> method.
	/// Blocks should be downloaded in parallel. Especially, if we get one block request and then immediately another one, BDS is supposed
	/// to start both tasks and not wait for the first one to finish. Meaning, BDS is not supposed to work in batches until it hits its
	/// level of parallelism (<see cref="BlockDownloadService.MaximumParallelTasks"/>).
	/// </summary>
	[Fact]
	public async Task TryGetBlockTests1Async()
	{
		using CancellationTokenSource testCts = new(TimeSpan.FromMinutes(1));

		uint256 blockHash1 = uint256.One;
		uint256 blockHash2 = new(2);
		uint256 blockHash3 = new(3);
		uint256 blockHash4 = new(4);

		Block block1 = Network.Main.Consensus.ConsensusFactory.CreateBlock();
		Block block2 = Network.Main.Consensus.ConsensusFactory.CreateBlock();
		Block block3 = Network.Main.Consensus.ConsensusFactory.CreateBlock();
		Block block4 = Network.Main.Consensus.ConsensusFactory.CreateBlock();

		TaskCompletionSource block1RequestedTcs = new();
		TaskCompletionSource block1DelayTcs = new();

		TaskCompletionSource block2RequestedTcs = new();
		TaskCompletionSource block2DelayTcs = new();
		TaskCompletionSource block2DownloadedTcs = new();

		Mock<IFileSystemBlockRepository> mockFileSystemBlockRepository = new(MockBehavior.Strict);
		_ = mockFileSystemBlockRepository.Setup(c => c.TryGetAsync(It.IsAny<uint256>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((Block?)null);

		_ = mockFileSystemBlockRepository.Setup(c => c.SaveAsync(It.IsAny<Block>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		Mock<IBlockProvider> mockFullNodeBlockProvider = new(MockBehavior.Strict);
		IBlockProvider fullNodeBlockProvider = mockFullNodeBlockProvider.Object;

		using (BlockDownloadService service = new(mockFileSystemBlockRepository.Object, [fullNodeBlockProvider], p2pBlockProvider: null, maximumParallelTasks: 3))
		{
			// Handling of downloading of block1.
			_ = mockFullNodeBlockProvider.Setup(c => c.TryGetBlockAsync(blockHash1, It.IsAny<CancellationToken>()))
				.Returns(async () =>
				{
					block1RequestedTcs.SetResult();

					// Wait until signal is given that "block is downloaded".
					await block1DelayTcs.Task.WaitAsync(testCts.Token).ConfigureAwait(false);

					return block1;
				});

			// Handling of downloading of block2.
			_ = mockFullNodeBlockProvider.SetupSequence(c => c.TryGetBlockAsync(blockHash2, It.IsAny<CancellationToken>()))
				.Returns(async () =>
				{
					block2RequestedTcs.SetResult();

					// Wait until signal is given that a download failure can be reported here.
					await block2DelayTcs.Task.WaitAsync(testCts.Token).ConfigureAwait(false);

					return null;
				})
				.ReturnsAsync(block2)
				.ReturnsAsync(block2);

			// Handling of downloading of block3.
			_ = mockFullNodeBlockProvider.Setup(c => c.TryGetBlockAsync(blockHash3, It.IsAny<CancellationToken>()))
				.ReturnsAsync(block3);

			// Handling of downloading of block4.
			_ = mockFullNodeBlockProvider.Setup(c => c.TryGetBlockAsync(blockHash4, It.IsAny<CancellationToken>()))
				.ReturnsAsync(block4);

			await service.StartAsync(testCts.Token);

			Task<IResult> task1 = service.TryGetBlockAsync(FullNodeSourceRequest.Instance, blockHash1, new Priority(SyncType.Complete, BlockHeight: 610_001), testCts.Token);
			Task<IResult> task2 = service.TryGetBlockAsync(FullNodeSourceRequest.Instance, blockHash2, new Priority(SyncType.Complete, BlockHeight: 610_002), testCts.Token);
			Task<IResult> task3 = service.TryGetBlockAsync(FullNodeSourceRequest.Instance, blockHash3, new Priority(SyncType.Complete, BlockHeight: 610_003), testCts.Token);
			Task<IResult> task4 = service.TryGetBlockAsync(FullNodeSourceRequest.Instance, blockHash4, new Priority(SyncType.Complete, BlockHeight: 610_004), testCts.Token);

			// Downloading of the block1 waits for our signal.
			{
				Assert.False(task1.IsCompletedSuccessfully);
				await block1RequestedTcs.Task.WaitAsync(testCts.Token);
			}

			// Add small delay to make sure that things stabilize.
			await Task.Delay(100);

			// Allow downloading of the block1.
			{
				Assert.False(task1.IsCompletedSuccessfully);
				block1DelayTcs.SetResult();

				// Block2 should be available even though block1 and block3 are waiting for data.
				IResult task1Result = await task1;
				SuccessResult successResult = Assert.IsType<SuccessResult>(task1Result);
				Assert.Same(block1, successResult.Block);
			}

			// Block3 should be available even though block1 and block3 are waiting for data.
			IResult resultBlock3 = await task3;

			// Verify that block2 waits for our signal, unblock it and verify result.
			{
				Assert.False(task2.IsCompletedSuccessfully);
				await block2RequestedTcs.Task.WaitAsync(testCts.Token);
				block2DelayTcs.SetResult();

				IResult task2Result = await task2;

				FailureResult failureResult = Assert.IsType<FailureResult>(task2Result);
				Assert.IsType<EmptySourceData>(failureResult.SourceData);
			}

			// All tasks should provide data now.
			Task<IResult>[] tasks = [task1, task2, task3, task4];
			Task.WaitAll(tasks);

			// Second attempt to download block2 should succeed.
			{
				IResult task2Result = await service.TryGetBlockAsync(FullNodeSourceRequest.Instance, blockHash2, new Priority(SyncType.Complete, BlockHeight: 610_002), testCts.Token);
				SuccessResult successResult = Assert.IsType<SuccessResult>(task2Result);
				Assert.Same(block2, successResult.Block);
			}

			await service.StopAsync(testCts.Token);
			await service.ExecuteTask!.WaitAsync(testCts.Token);
		}

		Block? actualBlock2 = await fullNodeBlockProvider.TryGetBlockAsync(blockHash2, testCts.Token);
		Assert.Same(block2, actualBlock2);

		mockFullNodeBlockProvider.VerifyAll();
	}

	/// <summary>
	/// Tests <see cref="BlockDownloadService.TryGetBlockAsync(uint256, Priority, uint, CancellationToken)"/>.
	/// </summary>
	[Fact]
	public async Task TryGetBlockTests2Async()
	{
		using CancellationTokenSource testCts = new(TimeSpan.FromMinutes(1));

		uint256 blockHash1 = uint256.One;
		uint256 blockHash2 = new(2);
		uint256 blockHash3 = new(3);
		uint256 blockHash4 = new(4);

		Block block1 = Network.Main.Consensus.ConsensusFactory.CreateBlock();
		Block block2 = Network.Main.Consensus.ConsensusFactory.CreateBlock();
		Block block3 = Network.Main.Consensus.ConsensusFactory.CreateBlock();
		Block block4 = Network.Main.Consensus.ConsensusFactory.CreateBlock();

		// File-system cache is disabled for the purposes of this test.
		Mock<IFileSystemBlockRepository> mockFileSystemBlockRepository = new(MockBehavior.Strict);
		_ = mockFileSystemBlockRepository.Setup(c => c.TryGetAsync(It.IsAny<uint256>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((Block?)null);

		_ = mockFileSystemBlockRepository.Setup(c => c.SaveAsync(It.IsAny<Block>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		Mock<IBlockProvider> mockFullNodeBlockProvider = new(MockBehavior.Strict);
		IBlockProvider fullNodeBlockProvider = mockFullNodeBlockProvider.Object;

		using (BlockDownloadService service = new(mockFileSystemBlockRepository.Object, [fullNodeBlockProvider], p2pBlockProvider: null, maximumParallelTasks: 3))
		{
			// Handling of downloading of block1.
			_ = mockFullNodeBlockProvider.Setup(c => c.TryGetBlockAsync(blockHash1, It.IsAny<CancellationToken>()))
				.ReturnsAsync(block1);

			// Handling of downloading of block2.
			_ = mockFullNodeBlockProvider.SetupSequence(c => c.TryGetBlockAsync(blockHash2, It.IsAny<CancellationToken>()))
				.ReturnsAsync((Block?)null)
				.ReturnsAsync(block2);

			// Handling of downloading of block3.
			_ = mockFullNodeBlockProvider.Setup(c => c.TryGetBlockAsync(blockHash3, It.IsAny<CancellationToken>()))
				.ReturnsAsync(block3);

			// Handling of downloading of block4.
			_ = mockFullNodeBlockProvider.Setup(c => c.TryGetBlockAsync(blockHash4, It.IsAny<CancellationToken>()))
				.ReturnsAsync(block4);

			await service.StartAsync(testCts.Token);

			IResult actualResult1 = await service.TryGetBlockAsync(FullNodeSourceRequest.Instance, blockHash1, new Priority(SyncType.Complete, BlockHeight: 610_001), testCts.Token);
			SuccessResult actualSuccessResult1 = Assert.IsType<SuccessResult>(actualResult1);
			Assert.Same(block1, actualSuccessResult1.Block);

			IResult actualResult2 = await service.TryGetBlockAsync(FullNodeSourceRequest.Instance, blockHash2, new Priority(SyncType.Complete, BlockHeight: 610_002), testCts.Token);
			FailureResult actualFailureResult2 = Assert.IsType<FailureResult>(actualResult2);
			Assert.IsType<EmptySourceData>(actualFailureResult2.SourceData);

			IResult actualResult3 = await service.TryGetBlockAsync(FullNodeSourceRequest.Instance, blockHash3, new Priority(SyncType.Complete, BlockHeight: 610_003), testCts.Token);
			SuccessResult actualSuccessResult3 = Assert.IsType<SuccessResult>(actualResult3);
			Assert.Same(block3, actualSuccessResult3.Block);

			IResult actualResult4 = await service.TryGetBlockAsync(FullNodeSourceRequest.Instance, blockHash4, new Priority(SyncType.Complete, BlockHeight: 610_004), testCts.Token);
			SuccessResult actualSuccessResult4 = Assert.IsType<SuccessResult>(actualResult4);
			Assert.Same(block4, actualSuccessResult4.Block);

			// Second attempt to get block2.
			actualResult2 = await service.TryGetBlockAsync(FullNodeSourceRequest.Instance, blockHash2, new Priority(SyncType.Complete, BlockHeight: 610_002), testCts.Token);
			SuccessResult actualSuccessResult2 = Assert.IsType<SuccessResult>(actualResult2);
			Assert.Same(block2, actualSuccessResult2.Block);

			// Getting a block over P2P fails because there is no P2P provider registered.
			IResult actualResult5 = await service.TryGetBlockAsync(P2pSourceRequest.Automatic, blockHash2, new Priority(SyncType.Complete, BlockHeight: 610_005), testCts.Token);
			NoSuchProviderResult actualSuccessResult5 = Assert.IsType<NoSuchProviderResult>(actualResult5);

			await service.StopAsync(testCts.Token);
			await service.ExecuteTask!.WaitAsync(testCts.Token);
		}

		mockFullNodeBlockProvider.VerifyAll();
	}

	[Fact]
	public async Task RemoveBlocksAsync()
	{
		using CancellationTokenSource testCts = new(TimeSpan.FromMinutes(1));

		uint256 blockHash1 = uint256.One;
		uint256 blockHash2 = new(2);
		uint256 blockHash3 = new(3);
		uint256 blockHash4 = new(4);

		Block block1 = Network.Main.Consensus.ConsensusFactory.CreateBlock();
		Block block2 = Network.Main.Consensus.ConsensusFactory.CreateBlock();
		Block block3 = Network.Main.Consensus.ConsensusFactory.CreateBlock();
		Block block4 = Network.Main.Consensus.ConsensusFactory.CreateBlock();

		Mock<IFileSystemBlockRepository> mockFileSystemBlockRepository = new(MockBehavior.Strict);
		_ = mockFileSystemBlockRepository.Setup(c => c.TryGetAsync(It.IsAny<uint256>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((Block?)null);
		_ = mockFileSystemBlockRepository.Setup(c => c.RemoveAsync(It.IsAny<uint256>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		Mock<IBlockProvider> mockFullNodeBlockProvider = new(MockBehavior.Strict);
		IBlockProvider fullNodeBlockProvider = mockFullNodeBlockProvider.Object;
		_ = mockFullNodeBlockProvider.Setup(c => c.TryGetBlockAsync(It.IsAny<uint256>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((Block?)null);

		using BlockDownloadService service = new(mockFileSystemBlockRepository.Object, [fullNodeBlockProvider], p2pBlockProvider: null, maximumParallelTasks: 3);

		// Intentionally, tested before the service is started just to smoke test that the queue is modified.
		Task<IResult>[] tasks = [
			service.TryGetBlockAsync(FullNodeSourceRequest.Instance, blockHash1, new Priority(SyncType.Complete, 610_001), testCts.Token),
			service.TryGetBlockAsync(FullNodeSourceRequest.Instance, blockHash2, new Priority(SyncType.Complete, 610_002), testCts.Token),
			service.TryGetBlockAsync(FullNodeSourceRequest.Instance, blockHash3, new Priority(SyncType.Complete, 610_003), testCts.Token),
			service.TryGetBlockAsync(FullNodeSourceRequest.Instance, blockHash4, new Priority(SyncType.Complete, 610_004), testCts.Token),
		];

		// Remove blocks >= 610_003.
		await service.RemoveBlocksAsync(maxBlockHeight: 610_003);

		Request[] actualRequests = service.BlocksToDownload.UnorderedItems
			.Select(x => x.Element)
			.OrderBy(x => x.Priority.BlockHeight)
			.ToArray();

		// Block 610_004 should be removed.
		Assert.Equal(3, actualRequests.Length);
		Assert.Equal(610_001u, actualRequests[0].Priority.BlockHeight);
		Assert.Equal(610_002u, actualRequests[1].Priority.BlockHeight);
		Assert.Equal(610_003u, actualRequests[2].Priority.BlockHeight);

		// Start the service late.
		await service.StartAsync(testCts.Token);

		Task.WaitAll(tasks);

		foreach (Task<IResult> blockDownloadTask in tasks.SkipLast(1))
		{
			IResult result = await blockDownloadTask;
			FailureResult failureResult = Assert.IsType<FailureResult>(result);
			Assert.IsType<EmptySourceData>(failureResult.SourceData);
		}

		foreach (Task<IResult> blockDownloadTask in tasks.Skip(3))
		{
			IResult result = await blockDownloadTask;
			ReorgOccurredResult reorgResult = Assert.IsType<ReorgOccurredResult>(result);
			Assert.Equal(610_003u, reorgResult.NewBlockchainHeight);
		}

		await service.StopAsync(testCts.Token);
	}
}

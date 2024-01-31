using NBitcoin;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Wallets;

namespace WalletWasabi.Tests.UnitTests.Wallet;

public class MockFileSystemBlockRepository : IFileSystemBlockRepository
{
	public MockFileSystemBlockRepository(Dictionary<uint256, Block> blocks)
	{
		Blocks = blocks;
	}

	public Dictionary<uint256, Block> Blocks { get; }

	public Task<Block?> TryGetAsync(uint256 id, CancellationToken cancellationToken)
		=> Task.FromResult(Blocks.GetValueOrDefault(id));

	public Task SaveAsync(Block element, CancellationToken cancellationToken)
		=> Task.CompletedTask;

	public Task RemoveAsync(uint256 blockHash, CancellationToken cancellationToken)
		=> Task.CompletedTask;

	public Task<int> CountAsync(CancellationToken cancellationToken)
		=> Task.FromResult(Blocks.Count);
}

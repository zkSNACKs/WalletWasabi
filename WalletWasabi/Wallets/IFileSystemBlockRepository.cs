using NBitcoin;
using System.Threading;
using System.Threading.Tasks;

namespace WalletWasabi.Wallets;

/// <summary>
/// File-system based block repository that allows to retrieve and store blocks.
/// </summary>
public interface IFileSystemBlockRepository
{
	Task<Block?> TryGetAsync(uint256 id, CancellationToken cancellationToken);

	Task SaveAsync(Block element, CancellationToken cancellationToken);

	/// <summary>
	/// Deletes a bitcoin block from the file system.
	/// </summary>
	/// <param name="blockHash">The block's hash that identifies the requested block.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <remarks>If the block is not cached, no exception is thrown.</remarks>
	Task RemoveAsync(uint256 blockHash, CancellationToken cancellationToken);

	Task<int> CountAsync(CancellationToken cancellationToken);
}

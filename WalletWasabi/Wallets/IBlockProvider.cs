using System.Threading;
using System.Threading.Tasks;
using NBitcoin;

namespace WalletWasabi.Wallets;

/// <summary>
/// IBlockProvider is an abstraction for types that can return blocks.
/// </summary>
public interface IBlockProvider
{
	/// <remarks>The implementations are not supposed to throw exceptions except <see cref="OperationCanceledException"/>.</remarks>
	Task<Block?> TryGetBlockAsync(uint256 blockHash, CancellationToken cancellationToken);

	/// <exception cref="InvalidOperationException">If the block cannot be obtained.</exception>
	async Task<Block> GetBlockAsync(uint256 blockHash, CancellationToken cancellationToken)
	{
		return await TryGetBlockAsync(blockHash, cancellationToken).ConfigureAwait(false)
			?? throw new InvalidOperationException($"Failed to get block {blockHash}.");
	}
}

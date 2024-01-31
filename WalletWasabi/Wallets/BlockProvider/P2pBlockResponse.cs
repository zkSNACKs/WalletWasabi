using NBitcoin;

namespace WalletWasabi.Wallets.BlockProvider;

public record P2pBlockResponse(Block? Block, ISourceData SourceData);

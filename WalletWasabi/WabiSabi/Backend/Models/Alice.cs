using NBitcoin;
using System;
using WalletWasabi.Crypto;
using WalletWasabi.Crypto.StrobeProtocol;

namespace WalletWasabi.WabiSabi.Backend.Models
{
	public class Alice
	{
		public Alice(Coin coin, OwnershipProof ownershipProof)
		{
			// TODO init syntax?
			Coin = coin;
			OwnershipProof = ownershipProof;
			Id = CalculateHash();
		}

		public uint256 Id { get; }
		public DateTimeOffset Deadline { get; set; } = DateTimeOffset.UtcNow;
		public Coin Coin { get; }
		public OwnershipProof OwnershipProof { get; }
		public Money TotalInputAmount => Coin.Amount;
		public int TotalInputVsize => Coin.ScriptPubKey.EstimateInputVsize();

		public bool ConfirmedConnection { get; set; } = false;
		public bool ReadyToSign { get; set; }

		public long CalculateRemainingVsizeCredentials(int maxRegistrableSize) => maxRegistrableSize - TotalInputVsize;

		public Money CalculateRemainingAmountCredentials(FeeRate feeRate) => Coin.EffectiveValue(feeRate);

		public void SetDeadlineRelativeTo(TimeSpan connectionConfirmationTimeout)
		{
			// Have alice timeouts a bit sooner than the timeout of connection confirmation phase.
			Deadline = DateTimeOffset.UtcNow + (connectionConfirmationTimeout * 0.9);
		}

		private uint256 CalculateHash() => CalculateHash(Coin, OwnershipProof);

		public static uint256 CalculateHash(Coin coin, OwnershipProof ownershipProof)
			=> StrobeHasher.Create(ProtocolConstants.AliceStrobeDomain)
				.Append(ProtocolConstants.AliceCoinTxOutStrobeLabel, coin.TxOut)
				.Append(ProtocolConstants.AliceCoinOutpointStrobeLabel, coin.Outpoint)
				.Append(ProtocolConstants.AliceOwnershipProofStrobeLabel, ownershipProof)
				.GetHash();
	}
}

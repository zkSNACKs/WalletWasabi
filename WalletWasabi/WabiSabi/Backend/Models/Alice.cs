using NBitcoin;
using Nito.AsyncEx;
using System;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Crypto;
using WalletWasabi.Crypto.StrobeProtocol;
using WalletWasabi.WabiSabi.Backend.Rounds;

namespace WalletWasabi.WabiSabi.Backend.Models
{
	public class Alice
	{
		public Alice(Coin coin, OwnershipProof ownershipProof, Round round)
		{
			// TODO init syntax?
			Round = round;
			Coin = coin;
			OwnershipProof = ownershipProof;
			Id = CalculateHash();
		}

		public Round Round { get; }
		public AsyncLock AsyncLock { get; } = new();

		public uint256 Id { get; }
		public DateTimeOffset Deadline { get; set; } = DateTimeOffset.UtcNow;
		public Coin Coin { get; }
		public OwnershipProof OwnershipProof { get; }
		public Money TotalInputAmount => Coin.Amount;
		public int TotalInputVsize => Coin.ScriptPubKey.EstimateInputVsize();

		public bool ConfirmedConnection { get; set; } = false;
		public bool ReadyToSign { get; set; }

		private CancellationTokenSource TimeoutCTS { get; } = new();

		public long CalculateRemainingVsizeCredentials(int maxRegistrableSize) => maxRegistrableSize - TotalInputVsize;

		public Money CalculateRemainingAmountCredentials(FeeRate feeRate) => Coin.EffectiveValue(feeRate);

		public async void Dispose()
		{
			// Don't cancel the timeout when it is
			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				TimeoutCTS.Cancel();
			}
		}

		public void SetDeadline(TimeSpan inputTimeout)
		{
			Deadline = DateTimeOffset.UtcNow + inputTimeout;
		}

		public async void StartTimeoutAsync(Arena arena)
		{
			var cancel = TimeoutCTS.Token;

			while (!cancel.IsCancellationRequested)
			{
				while (Deadline - DateTimeOffset.UtcNow is var delay && delay > TimeSpan.Zero)
				{
					await Task.Delay(delay, cancel).ConfigureAwait(false);
				}

				// Alice expired, try to remove it
				using (await AsyncLock.LockAsync(cancel).ConfigureAwait(false))
				{
					// By the time we acquired the lock, Alice re-confirmed,
					// in which case we go back to the loop.
					if (Deadline - DateTimeOffset.UtcNow is var delay && delay <= TimeSpan.Zero)
					{
						await Round.TimeoutAliceAsync(this, arena, cancel);
						return;
					}
				}
			}
		}

		private uint256 CalculateHash()
			=> StrobeHasher.Create(ProtocolConstants.AliceStrobeDomain)
				.Append(ProtocolConstants.AliceCoinTxOutStrobeLabel, Coin.TxOut)
				.Append(ProtocolConstants.AliceCoinOutpointStrobeLabel, Coin.Outpoint)
				.Append(ProtocolConstants.AliceOwnershipProofStrobeLabel, OwnershipProof)
				.GetHash();
	}
}

using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WalletWasabi.WabiSabi.Client
{
	public class BobClient
	{
		public BobClient(uint256 roundId, ArenaClient arenaClient)
		{
			RoundId = roundId;
			ArenaClient = arenaClient;
		}

		private uint256 RoundId { get; }
		private ArenaClient ArenaClient { get; }

		public async Task RegisterOutputAsync(Money amount, Script scriptPubKey)
		{
			await ArenaClient.RegisterOutputAsync(
				RoundId,
				amount.Satoshi,
				scriptPubKey,
				await ArenaClient.AmountCredentialClient.Credentials.TakeAsync(amount.Satoshi),
				await ArenaClient.VsizeCredentialClient.Credentials.TakeAsync(scriptPubKey.EstimateOutputVsize())).ConfigureAwait(false);
		}
	}
}

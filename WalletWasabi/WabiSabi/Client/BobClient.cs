using NBitcoin;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Crypto.ZeroKnowledge;

namespace WalletWasabi.WabiSabi.Client
{
	public class BobClient
	{
		public BobClient(uint256 roundId, ArenaClient arenaClient)
		{
			RoundId = roundId;
			ArenaClient = arenaClient;
		}

		public uint256 RoundId { get; }
		private ArenaClient ArenaClient { get; }

		public async Task RegisterOutputAsync(Script scriptPubKey, IEnumerable<Credential> amountCredentials, IEnumerable<Credential> vsizeCredentials, CancellationToken cancellationToken)
		{
			await ArenaClient.RegisterOutputAsync(
				RoundId,
				scriptPubKey,
				amountCredentials,
				vsizeCredentials,
				cancellationToken).ConfigureAwait(false);
		}

		public async Task<(IEnumerable<Credential> RealAmountCredentials, IEnumerable<Credential> RealVsizeCredentials)> ReissueCredentialsAsync(
			IEnumerable<long> amountsToRequest,
			IEnumerable<long> vsizesToRequest,
			IEnumerable<Credential> amountCredential,
			IEnumerable<Credential> vsizeCredential,
			CancellationToken cancellationToken)
		{
			var response = await ArenaClient.ReissueCredentialAsync(
				RoundId,
				amountsToRequest,
				vsizesToRequest,
				amountCredential,
				vsizeCredential,
				cancellationToken)
				.ConfigureAwait(false);

			return (response.IssuedAmountCredentials, response.IssuedVsizeCredentials);
		}
	}
}

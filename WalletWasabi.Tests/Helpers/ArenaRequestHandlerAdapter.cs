using System;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Models;

namespace WalletWasabi.Tests.Helpers
{
	public class ArenaRequestHandlerAdapter : IWabiSabiApiRequestHandler
	{
		private readonly Arena arena;

		public ArenaRequestHandlerAdapter(Arena arena)
		{
			this.arena = arena;
		}

		public Task<ConnectionConfirmationResponse> ConfirmConnectionAsync(ConnectionConfirmationRequest request, CancellationToken cancellationToken)
			=> arena.ConfirmConnectionAsync(request);

		public Task<RoundState[]> GetStatusAsync(CancellationToken cancellationToken)
			=> throw new NotImplementedException();

		public Task<InputRegistrationResponse> RegisterInputAsync(InputRegistrationRequest request, CancellationToken cancellationToken)
			=> arena.RegisterInputAsync((request));

		public Task RegisterOutputAsync(OutputRegistrationRequest request, CancellationToken cancellationToken)
			=> arena.RegisterOutputAsync(request);

		public Task<ReissueCredentialResponse> ReissueCredentialAsync(ReissueCredentialRequest request, CancellationToken cancellationToken)
			=> arena.ReissuanceAsync(request);

		public Task RemoveInputAsync(InputsRemovalRequest request, CancellationToken cancellationToken)
			=> arena.RemoveInputAsync(request);

		public Task SignTransactionAsync(TransactionSignaturesRequest request, CancellationToken cancellationToken)
			=> arena.SignTransactionAsync(request);

		public Task ReadyToSign(ReadyToSignRequestRequest request, CancellationToken cancellationToken)
			=> arena.ReadyToSignAsync(request);
	}
}

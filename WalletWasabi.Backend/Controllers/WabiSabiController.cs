using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using WalletWasabi.Backend.Filters;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Models;

namespace WalletWasabi.Backend.Controllers
{
	[ApiController]
	[ExceptionTranslate]
	[Route("[controller]")]
	[Produces("application/json")]
	public class WabiSabiController : ControllerBase, IWabiSabiApiRequestHandler
	{
		public WabiSabiController(ArenaRequestHandler handler)
		{
			RequestHandler = handler;
		}

		private ArenaRequestHandler RequestHandler { get; }

		[HttpGet("status")]
		public Task<RoundState[]> GetStatusAsync(CancellationToken cancellationToken)
		{
			return RequestHandler.GetStatusAsync(cancellationToken);
		}

		[HttpPost("connection-confirmation")]
		[Idempotent]
		public Task<ConnectionConfirmationResponse> ConfirmConnectionAsync(ConnectionConfirmationRequest request, CancellationToken cancellableToken)
		{
			return RequestHandler.ConfirmConnectionAsync(request, cancellableToken);
		}

		[HttpPost("input-registration")]
		[Idempotent]
		public Task<InputRegistrationResponse> RegisterInputAsync(InputRegistrationRequest request, CancellationToken cancellableToken)
		{
			return RequestHandler.RegisterInputAsync(request, cancellableToken);
		}

		[HttpPost("output-registration")]
		[Idempotent]
		public Task RegisterOutputAsync(OutputRegistrationRequest request, CancellationToken cancellableToken)
		{
			return RequestHandler.RegisterOutputAsync(request, cancellableToken);
		}

		[HttpPost("credential-issuance")]
		[Idempotent]
		public Task<ReissueCredentialResponse> ReissueCredentialAsync(ReissueCredentialRequest request, CancellationToken cancellableToken)
		{
			return RequestHandler.ReissueCredentialAsync(request, cancellableToken);
		}

		[HttpPost("input-unregistration")]
		public Task RemoveInputAsync(InputsRemovalRequest request, CancellationToken cancellableToken)
		{
			return RequestHandler.RemoveInputAsync(request, cancellableToken);
		}

		[HttpPost("transaction-signature")]
		public Task SignTransactionAsync(TransactionSignaturesRequest request, CancellationToken cancellableToken)
		{
			return RequestHandler.SignTransactionAsync(request, cancellableToken);
		}

		[HttpPost("ready-to-sign")]
		public Task ReadyToSign(ReadyToSignRequestRequest request, CancellationToken cancellableToken)
		{
			return RequestHandler.ReadyToSignAsync(request, cancellableToken);
		}
	}
}

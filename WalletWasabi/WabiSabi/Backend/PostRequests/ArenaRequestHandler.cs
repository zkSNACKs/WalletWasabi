using NBitcoin;
using System;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Nito.AsyncEx;
using WalletWasabi.WabiSabi.Backend.Banning;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Models;

namespace WalletWasabi.WabiSabi.Backend.PostRequests
{
	public class ArenaRequestHandler : IAsyncDisposable
	{
		public ArenaRequestHandler(WabiSabiConfig config, Prison prison, Arena arena, IRPCClient rpc)
		{
			Config = config;
			Prison = prison;
			Arena = arena;
			Rpc = rpc;
			Network = rpc.Network;
		}

		private bool DisposeStarted { get; set; } = false;
		private object DisposeStartedLock { get; } = new();
		private AbandonedTasks RunningRequests { get; } = new();
		public WabiSabiConfig Config { get; }
		public Prison Prison { get; }
		public Arena Arena { get; }
		public IRPCClient Rpc { get; }
		public Network Network { get; }

		public async Task<InputRegistrationResponse> RegisterInputAsync(InputRegistrationRequest request, CancellationToken cancellationToken)
		{
			DisposeGuard();
			using (RunningTasks.RememberWith(RunningRequests))
			{
				return await Arena.RegisterInputAsync(request).ConfigureAwait(false);
			}
		}

		public async Task RemoveInputAsync(InputsRemovalRequest request, CancellationToken cancellationToken)
		{
			DisposeGuard();
			using (RunningTasks.RememberWith(RunningRequests))
			{
				await Arena.RemoveInputAsync(request).ConfigureAwait(false);
			}
		}

		public async Task<ConnectionConfirmationResponse> ConfirmConnectionAsync(ConnectionConfirmationRequest request, CancellationToken cancellationToken)
		{
			DisposeGuard();
			using (RunningTasks.RememberWith(RunningRequests))
			{
				return await Arena.ConfirmConnectionAsync(request).ConfigureAwait(false);
			}
		}

		public async Task<OutputRegistrationResponse> RegisterOutputAsync(OutputRegistrationRequest request, CancellationToken cancellationToken)
		{
			DisposeGuard();
			using (RunningTasks.RememberWith(RunningRequests))
			{
				return await Arena.RegisterOutputAsync(request).ConfigureAwait(false);
			}
		}

		public async Task SignTransactionAsync(TransactionSignaturesRequest request, CancellationToken cancellationToken)
		{
			DisposeGuard();
			using (RunningTasks.RememberWith(RunningRequests))
			{
				await Arena.SignTransactionAsync(request).ConfigureAwait(false);
			}
		}

		public async Task<ReissueCredentialResponse> ReissueCredentialAsync(ReissueCredentialRequest request, CancellationToken cancellationToken)
		{
			DisposeGuard();
			using (RunningTasks.RememberWith(RunningRequests))
			{
				return Arena.ReissueCredentials(request);
			}
		}

		public async Task ReadyToSignAsync(ReadyToSignRequestRequest request, CancellationToken cancellableToken)
		{
			DisposeGuard();
			using (RunningTasks.RememberWith(RunningRequests))
			{
				await Arena.ReadyToSignAsync(request).ConfigureAwait(false);
			}
		}

		public async Task<RoundState[]> GetStatusAsync(CancellationToken cancellationToken)
		{
			DisposeGuard();
			using (RunningTasks.RememberWith(RunningRequests))
			{
				return await Arena.GetStatusAsync(cancellationToken).ConfigureAwait(false);
			}
		}

		public async ValueTask DisposeAsync()
		{
			lock (DisposeStartedLock)
			{
				DisposeStarted = true;
			}
			await RunningRequests.WhenAllAsync().ConfigureAwait(false);
		}

		private void DisposeGuard()
		{
			lock (DisposeStartedLock)
			{
				if (DisposeStarted)
				{
					throw new ObjectDisposedException(nameof(ArenaRequestHandler));
				}
			}
		}
	}
}

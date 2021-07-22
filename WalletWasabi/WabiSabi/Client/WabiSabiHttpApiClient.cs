using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WalletWasabi.Tor.Http;
using WalletWasabi.Tor.Http.Extensions;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.Serialization;

namespace WalletWasabi.WabiSabi.Client
{
	public class WabiSabiHttpApiClient : IWabiSabiApiRequestHandler
	{
		private IHttpClient _client;

		public WabiSabiHttpApiClient(IHttpClient client)
		{
			_client = client;
		}

		private enum RemoteAction
		{
			RegisterInput,
			RemoveInput,
			ConfirmConnection,
			RegisterOutput,
			ReissueCredential,
			SignTransaction,
			GetStatus,
			ReadyToSign
		}

		public Task<InputRegistrationResponse> RegisterInputAsync(InputRegistrationRequest request, CancellationToken cancellationToken) =>
			SendAndReceiveAsync<InputRegistrationRequest, InputRegistrationResponse>(RemoteAction.RegisterInput, request, cancellationToken);

		public Task<ConnectionConfirmationResponse> ConfirmConnectionAsync(ConnectionConfirmationRequest request, CancellationToken cancellationToken) =>
			SendAndReceiveAsync<ConnectionConfirmationRequest, ConnectionConfirmationResponse>(RemoteAction.ConfirmConnection, request, cancellationToken);

		public Task RegisterOutputAsync(OutputRegistrationRequest request, CancellationToken cancellationToken) =>
			SendAndReceiveAsync<OutputRegistrationRequest>(RemoteAction.RegisterOutput, request, cancellationToken);

		public Task<ReissueCredentialResponse> ReissueCredentialAsync(ReissueCredentialRequest request, CancellationToken cancellationToken) =>
			SendAndReceiveAsync<ReissueCredentialRequest, ReissueCredentialResponse>(RemoteAction.ReissueCredential, request, cancellationToken);

		public Task RemoveInputAsync(InputsRemovalRequest request, CancellationToken cancellationToken) =>
			SendAndReceiveAsync<InputsRemovalRequest>(RemoteAction.RemoveInput, request, cancellationToken);

		public virtual Task SignTransactionAsync(TransactionSignaturesRequest request, CancellationToken cancellationToken) =>
			SendAndReceiveAsync<TransactionSignaturesRequest>(RemoteAction.SignTransaction, request, cancellationToken);

		public async Task<RoundState[]> GetStatusAsync(CancellationToken cancellationToken)
		{
			using var response = await _client.SendAsync(HttpMethod.Get, GetUriEndPoint(RemoteAction.GetStatus), cancel: cancellationToken).ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				await response.ThrowRequestExceptionFromContentAsync(cancellationToken).ConfigureAwait(false);
			}
			var jsonString = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return Deserialize<RoundState[]>(jsonString);
		}

		public Task ReadyToSign(ReadyToSignRequestRequest request, CancellationToken cancellationToken) =>
			SendAndReceiveAsync<ReadyToSignRequestRequest>(RemoteAction.ReadyToSign, request, cancellationToken);

		private async Task<string> SendAsync<TRequest>(RemoteAction action, TRequest request, CancellationToken cancellationToken) where TRequest : class
		{
			using var content = Serialize(request);
			using var response = await _client.SendAsync(HttpMethod.Post, GetUriEndPoint(action), content, cancellationToken).ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				await response.ThrowRequestExceptionFromContentAsync(cancellationToken).ConfigureAwait(false);
			}
			return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		}

		private async Task SendAndReceiveAsync<TRequest>(RemoteAction action, TRequest request, CancellationToken cancellationToken) where TRequest : class
		{
			await SendAsync(action, request, cancellationToken).ConfigureAwait(false);
		}

		private async Task<TResponse> SendAndReceiveAsync<TRequest, TResponse>(RemoteAction action, TRequest request, CancellationToken cancellationToken) where TRequest : class
		{
			var jsonString = await SendAsync(action, request, cancellationToken).ConfigureAwait(false);
			return Deserialize<TResponse>(jsonString);
		}

		private static StringContent Serialize<T>(T obj)
		{
			string jsonString = JsonConvert.SerializeObject(obj, JsonSerializationOptions.Default.Settings);
			return new StringContent(jsonString, Encoding.UTF8, "application/json");
		}

		private static TResponse Deserialize<TResponse>(string jsonString)
		{
			return JsonConvert.DeserializeObject<TResponse>(jsonString, JsonSerializationOptions.Default.Settings)
				?? throw new InvalidOperationException("Deserialization error");
		}

		private static string GetUriEndPoint(RemoteAction action) =>
			"wabisabi/" + action switch
			{
				RemoteAction.RegisterInput => "input-registration",
				RemoteAction.RegisterOutput => "output-registration",
				RemoteAction.ConfirmConnection => "connection-confirmation",
				RemoteAction.ReissueCredential => "credential-issuance",
				RemoteAction.RemoveInput => "input-unregistration",
				RemoteAction.SignTransaction => "transaction-signature",
				RemoteAction.GetStatus => "status",
				RemoteAction.ReadyToSign => "ready-to-sign",
				_ => throw new NotSupportedException($"Action '{action}' is unknown and has no endpoint associated.")
			};
	}
}

using NBitcoin;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WabiSabi.Crypto;
using WabiSabi.Crypto.ZeroKnowledge;
using WalletWasabi.Crypto;
using WalletWasabi.Helpers;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;

namespace WalletWasabi.WabiSabi.Client.CoinJoin.Client;

public class ArenaClient
{
	public ArenaClient(
		WabiSabiClient amountCredentialClient,
		WabiSabiClient vsizeCredentialClient,
		string coordinatorIdentifier,
		IWabiSabiApiRequestHandler requestHandler)
	{
		AmountCredentialClient = amountCredentialClient;
		VsizeCredentialClient = vsizeCredentialClient;
		CoordinatorIdentifier = coordinatorIdentifier;
		RequestHandler = requestHandler;
	}

	public WabiSabiClient AmountCredentialClient { get; }
	public WabiSabiClient VsizeCredentialClient { get; }
	public string CoordinatorIdentifier { get; }
	public IWabiSabiApiRequestHandler RequestHandler { get; }

	public async Task<ArenaResponse<Guid>> RegisterInputAsync(
		uint256 roundId,
		OutPoint outPoint,
		OwnershipProof ownershipProof,
		CancellationToken cancellationToken)
	{
		var zeroAmountCredentialRequestData = AmountCredentialClient.CreateRequestForZeroAmount();
		var zeroVsizeCredentialRequestData = VsizeCredentialClient.CreateRequestForZeroAmount();

		var inputRegistrationResponse = await RequestHandler.RegisterInputAsync(
			new InputRegistrationRequest(
				roundId,
				outPoint,
				ownershipProof,
				zeroAmountCredentialRequestData.CredentialsRequest,
				zeroVsizeCredentialRequestData.CredentialsRequest),
			cancellationToken).ConfigureAwait(false);

		var realAmountCredentials = AmountCredentialClient.HandleResponse(inputRegistrationResponse.AmountCredentials, zeroAmountCredentialRequestData.CredentialsResponseValidation);
		var realVsizeCredentials = VsizeCredentialClient.HandleResponse(inputRegistrationResponse.VsizeCredentials, zeroVsizeCredentialRequestData.CredentialsResponseValidation);

		return new(inputRegistrationResponse.AliceId, realAmountCredentials, realVsizeCredentials);
	}

	public async Task RemoveInputAsync(uint256 roundId, Guid aliceId, CancellationToken cancellationToken)
	{
		await RequestHandler.RemoveInputAsync(new InputsRemovalRequest(roundId, aliceId), cancellationToken).ConfigureAwait(false);
	}

	public async Task RegisterOutputAsync(
		uint256 roundId,
		Script scriptPubKey,
		IEnumerable<Credential> amountCredentialsToPresent,
		IEnumerable<Credential> vsizeCredentialsToPresent,
		CancellationToken cancellationToken)
	{
		Guard.InRange(nameof(amountCredentialsToPresent), amountCredentialsToPresent, 0, AmountCredentialClient.NumberOfCredentials);
		Guard.InRange(nameof(vsizeCredentialsToPresent), vsizeCredentialsToPresent, 0, VsizeCredentialClient.NumberOfCredentials);

		var presentedAmount = amountCredentialsToPresent.Sum(x => x.Value);
		var (realAmountCredentialRequest, realAmountCredentialResponseValidation) = AmountCredentialClient.CreateRequest(
			amountCredentialsToPresent,
			cancellationToken);

		var presentedVsize = vsizeCredentialsToPresent.Sum(x => x.Value);
		var (realVsizeCredentialRequest, realVsizeCredentialResponseValidation) = VsizeCredentialClient.CreateRequest(
			vsizeCredentialsToPresent,
			cancellationToken);

		await RequestHandler.RegisterOutputAsync(
			new OutputRegistrationRequest(
				roundId,
				scriptPubKey,
				realAmountCredentialRequest,
				realVsizeCredentialRequest),
			cancellationToken).ConfigureAwait(false);
	}

	public async Task<ArenaResponse> ReissueCredentialAsync(
		uint256 roundId,
		IEnumerable<long> amountsToRequest,
		IEnumerable<long> vsizesToRequest,
		IEnumerable<Credential> amountCredentialsToPresent,
		IEnumerable<Credential> vsizeCredentialsToPresent,
		CancellationToken cancellationToken)
	{
		Guard.InRange(nameof(amountCredentialsToPresent), amountCredentialsToPresent, 0, AmountCredentialClient.NumberOfCredentials);

		var presentedAmount = amountCredentialsToPresent.Sum(x => x.Value);
		if (amountsToRequest.Sum() != presentedAmount)
		{
			throw new InvalidOperationException($"Reissuance amounts sum must equal the sum of the presented ones.");
		}

		var presentedVsize = vsizeCredentialsToPresent.Sum(x => x.Value);
		if (vsizesToRequest.Sum() > presentedVsize)
		{
			throw new InvalidOperationException($"Reissuance vsizes sum can not be greater than the sum of the presented ones.");
		}

		var (realVsizeCredentialRequest, realVsizeCredentialResponseValidation) = VsizeCredentialClient.CreateRequest(
			vsizesToRequest,
			vsizeCredentialsToPresent,
			cancellationToken);

		var (realAmountCredentialRequest, realAmountCredentialResponseValidation) = AmountCredentialClient.CreateRequest(
			amountsToRequest,
			amountCredentialsToPresent,
			cancellationToken);

		var zeroAmountCredentialRequestData = AmountCredentialClient.CreateRequestForZeroAmount();
		var zeroVsizeCredentialRequestData = VsizeCredentialClient.CreateRequestForZeroAmount();

		var reissuanceResponse = await RequestHandler.ReissuanceAsync(
			new ReissueCredentialRequest(
				roundId,
				realAmountCredentialRequest,
				realVsizeCredentialRequest,
				zeroAmountCredentialRequestData.CredentialsRequest,
				zeroVsizeCredentialRequestData.CredentialsRequest),
			cancellationToken).ConfigureAwait(false);

		var realAmountCredentials = AmountCredentialClient.HandleResponse(reissuanceResponse.RealAmountCredentials, realAmountCredentialResponseValidation);
		var realVsizeCredentials = VsizeCredentialClient.HandleResponse(reissuanceResponse.RealVsizeCredentials, realVsizeCredentialResponseValidation);
		var zeroAmountCredentials = AmountCredentialClient.HandleResponse(reissuanceResponse.ZeroAmountCredentials, zeroAmountCredentialRequestData.CredentialsResponseValidation);
		var zeroVsizeCredentials = VsizeCredentialClient.HandleResponse(reissuanceResponse.ZeroVsizeCredentials, zeroVsizeCredentialRequestData.CredentialsResponseValidation);

		return new(realAmountCredentials.Concat(zeroAmountCredentials), realVsizeCredentials.Concat(zeroVsizeCredentials));
	}

	public async Task<ArenaResponse<bool>> ConfirmConnectionAsync(
		uint256 roundId,
		Guid aliceId,
		IEnumerable<long> amountsToRequest,
		IEnumerable<long> vsizesToRequest,
		IEnumerable<Credential> amountCredentialsToPresent,
		IEnumerable<Credential> vsizeCredentialsToPresent,
		CancellationToken cancellationToken)
	{
		Guard.InRange(nameof(amountsToRequest), amountsToRequest, 1, ProtocolConstants.CredentialNumber);
		Guard.InRange(nameof(amountCredentialsToPresent), amountCredentialsToPresent, 0, ProtocolConstants.CredentialNumber);
		Guard.InRange(nameof(vsizeCredentialsToPresent), vsizeCredentialsToPresent, 0, ProtocolConstants.CredentialNumber);
		Guard.InRange(nameof(vsizesToRequest), vsizesToRequest, 1, VsizeCredentialClient.NumberOfCredentials);

		var realAmountCredentialRequestData = AmountCredentialClient.CreateRequest(
			amountsToRequest,
			amountCredentialsToPresent,
			cancellationToken);

		var realVsizeCredentialRequestData = VsizeCredentialClient.CreateRequest(
			vsizesToRequest,
			vsizeCredentialsToPresent,
			cancellationToken);

		var zeroAmountCredentialRequestData = AmountCredentialClient.CreateRequestForZeroAmount();
		var zeroVsizeCredentialRequestData = VsizeCredentialClient.CreateRequestForZeroAmount();

		var confirmConnectionResponse = await RequestHandler.ConfirmConnectionAsync(
			new ConnectionConfirmationRequest(
				roundId,
				aliceId,
				zeroAmountCredentialRequestData.CredentialsRequest,
				realAmountCredentialRequestData.CredentialsRequest,
				zeroVsizeCredentialRequestData.CredentialsRequest,
				realVsizeCredentialRequestData.CredentialsRequest),
			cancellationToken).ConfigureAwait(false);

		var zeroAmountCredentials = AmountCredentialClient.HandleResponse(confirmConnectionResponse.ZeroAmountCredentials, zeroAmountCredentialRequestData.CredentialsResponseValidation);
		var zeroVsizeCredentials = VsizeCredentialClient.HandleResponse(confirmConnectionResponse.ZeroVsizeCredentials, zeroVsizeCredentialRequestData.CredentialsResponseValidation);

		if (confirmConnectionResponse is { RealAmountCredentials: { }, RealVsizeCredentials: { } })
		{
			var realAmountCredentials = AmountCredentialClient.HandleResponse(confirmConnectionResponse.RealAmountCredentials, realAmountCredentialRequestData.CredentialsResponseValidation);
			var realVsizeCredentials = VsizeCredentialClient.HandleResponse(confirmConnectionResponse.RealVsizeCredentials, realVsizeCredentialRequestData.CredentialsResponseValidation);
			return new(true, realAmountCredentials.Concat(zeroAmountCredentials), realVsizeCredentials.Concat(zeroVsizeCredentials));
		}

		return new(false, zeroAmountCredentials, zeroVsizeCredentials);
	}

	public async Task SignTransactionAsync(
		uint256 roundId,
		Coin coin,
		IKeyChain keyChain, // unused now
		TransactionWithPrecomputedData unsignedCoinJoin,
		CancellationToken cancellationToken)
	{
		var signedCoinJoin = keyChain.Sign(unsignedCoinJoin.Transaction, coin, unsignedCoinJoin.PrecomputedTransactionData);
		var txInput = signedCoinJoin.Inputs.AsIndexedInputs().First(input => input.PrevOut == coin.Outpoint);
		if (!txInput.VerifyScript(coin, ScriptVerify.Standard, unsignedCoinJoin.PrecomputedTransactionData, out var error))
		{
			throw new InvalidOperationException($"Witness is missing. Reason {nameof(ScriptError)} code: {error}.");
		}

		await RequestHandler.SignTransactionAsync(new TransactionSignaturesRequest(roundId, txInput.Index, txInput.WitScript), cancellationToken).ConfigureAwait(false);
	}

	public async Task<RoundStateResponse> GetStatusAsync(RoundStateRequest request, CancellationToken cancellationToken)
	{
		return await RequestHandler.GetStatusAsync(request, cancellationToken).ConfigureAwait(false);
	}

	public async Task ReadyToSignAsync(
		uint256 roundId,
		Guid aliceId,
		CancellationToken cancellationToken)
	{
		await RequestHandler.ReadyToSignAsync(
			new ReadyToSignRequestRequest(roundId, aliceId),
			cancellationToken).ConfigureAwait(false);
	}
}

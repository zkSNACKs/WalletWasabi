using System.Linq;
using System.Collections.Generic;
using WalletWasabi.Crypto.Groups;
using WalletWasabi.Crypto.ZeroKnowledge;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace WalletWasabi.WabiSabi.Crypto.CredentialRequesting
{
	/// <summary>
	/// Represents a request message for the WabiSabi unified registration protocol.
	/// </summary>
	/// <remarks>
	/// RegistrationRequestMessage message is the unified protocol message used in both phases,
	/// inputs registration and outputs registration and it is designed to support
	/// credentials reissuance.
	/// </remarks>
	public abstract class CredentialsRequest
	{
		[JsonConstructor]
		internal CredentialsRequest(
			long delta,
			IEnumerable<CredentialPresentation> presented,
			IEnumerable<IssuanceRequest> requested,
			IEnumerable<Proof> proofs)
		{
			Delta = delta;
			Presented = presented;
			Requested = requested;
			Proofs = proofs;
		}

		/// <summary>
		/// The difference between the sum of the requested credentials and the presented credentials.
		/// </summary>
		/// <remarks>
		/// A positive value of this property indicates that the request is an inputs registration request,
		/// a negative value indicates it is an outputs registration request, while finally a zero value
		/// indicates it is a reissuance request or a request for zero-value credentials.
		/// </remarks>
		public long Delta { get; }

		/// <summary>
		/// Randomized credentials presented for output registration or reissuance.
		/// </summary>
		public IEnumerable<CredentialPresentation> Presented { get; }

		/// <summary>
		/// Credential isssuance requests.
		/// </summary>
		public IEnumerable<IssuanceRequest> Requested { get; }

		/// <summary>
		/// Accompanying range and sum proofs to the coordinator.
		/// </summary>
		public IEnumerable<Proof> Proofs { get; }

		/// <summary>
		/// Is request for zero-value credentials only.
		/// </summary>
		[JsonIgnore]
		internal bool IsNullRequest => Delta == 0 && !Presented.Any();

		/// <summary>
		/// Is request for credential presentation only.
		/// </summary>
		[JsonIgnore]
		internal bool IsPresentationOnlyRequest => Delta < 0 && !Requested.Any();

		/// <summary>
		/// Serial numbers used in the credential presentations.
		/// </summary>
		[JsonIgnore]
		internal IEnumerable<GroupElement> SerialNumbers => Presented.Select(x => x.S);

		/// <summary>
		/// Indicates whether the message contains duplicated serial numbers or not.
		/// </summary>
		[JsonIgnore]
		internal bool AreThereDuplicatedSerialNumbers => SerialNumbers.Distinct().Count() < SerialNumbers.Count();
	}
}

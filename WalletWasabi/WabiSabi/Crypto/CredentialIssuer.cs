using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using NBitcoin;
using NBitcoin.Secp256k1;
using WalletWasabi.Crypto;
using WalletWasabi.Crypto.Groups;
using WalletWasabi.Crypto.Randomness;
using WalletWasabi.Crypto.ZeroKnowledge;
using WalletWasabi.Crypto.ZeroKnowledge.LinearRelation;
using WalletWasabi.Helpers;
using WalletWasabi.WabiSabi.Crypto.CredentialRequesting;

namespace WalletWasabi.WabiSabi.Crypto
{
	/// <summary>
	/// Issues anonymous credentials using the coordinator's secret key.
	/// </summary>
	/// <remarks>
	/// CredentialIssuer is the coordinator's component used to issue anonymous credentials
	/// requested by a WabiSabi client. This means this component abstracts receives
	/// <see cref="CredentialsRequest">RegistrationRequests</see>, validates the requested
	/// amounts are in the valid range, serial numbers are not duplicated nor reused,
	/// and finally issues the credentials using the coordinator's secret key (and also
	/// proving to the WabiSabi client that the credentials were issued with the right
	/// key).
	///
	/// Note that this is a stateful component because it needs to keep track of the
	/// presented credentials' serial numbers in order to prevent credential
	/// reuse. The API is concurrency safe. Additionally it keeps also track of
	/// the <see cref="Balance">balance</see> in order to make sure it never
	/// issues credentials for more money than the total presented amount. All
	/// this means that the same instance has to be used for a given round (the
	/// coordinator needs to maintain only one instance of this class per round)
	///
	/// About replay requests: a replay request is a <see cref="CredentialsRequest">request</see>
	/// that has already been seen before. These kind of requests can be the result of misbehaving
	/// clients or simply clients using a retry communication mechanism.
	/// Reply requests are not handled by this component and they have to be handled by a different
	/// component. A good solution is to use a caching feature where the request fingerprint is
	/// used as a key, in this way using a standard solution it is possible to respond with the exact
	/// same valid credentials to the client without performance penalties.
	/// </remarks>
	public class CredentialIssuer
	{
		// Canary test check to ensure credential balance is never negative.
		// Accessed using Interlocked methods.
		private long _balance = 0;

		/// <summary>
		/// Initializes a new instance of the CredentialIssuer class.
		/// </summary>
		/// <param name="credentialIssuerSecretKey">The <see cref="CredentialIssuerSecretKey">coordinator's secret key</see> used to issue the credentials.</param>
		/// <param name="randomNumberGenerator">The random number generator.</param>
		public CredentialIssuer(
			CredentialIssuerSecretKey credentialIssuerSecretKey,
			WasabiRandom randomNumberGenerator,
			long maxAmount)
		{
			MaxAmount = maxAmount;
			RangeProofWidth = (int)Math.Ceiling(Math.Log2(MaxAmount));
			CredentialIssuerSecretKey = Guard.NotNull(nameof(credentialIssuerSecretKey), credentialIssuerSecretKey);
			CredentialIssuerParameters = CredentialIssuerSecretKey.ComputeCredentialIssuerParameters();
			RandomNumberGenerator = Guard.NotNull(nameof(randomNumberGenerator), randomNumberGenerator);
		}

		public long MaxAmount { get; }

		public int RangeProofWidth { get; }

		// Keeps track of the used serial numbers. This is part of
		// the double-spending prevention mechanism.
		private ConcurrentDictionary<GroupElement, bool> SerialNumbers { get; } = new();

		public long Balance => Interlocked.Read(ref _balance);

		private WasabiRandom RandomNumberGenerator { get; }

		public CredentialIssuerSecretKey CredentialIssuerSecretKey { get; }

		private CredentialIssuerParameters CredentialIssuerParameters { get; }

		/// <summary>
		/// Gets the number of credentials that have to be requested/presented
		/// This parameter is called `k` in the WabiSabi paper.
		/// </summary>
		public int NumberOfCredentials => ProtocolConstants.CredentialNumber;

		/// <summary>
		/// Process the <see cref="CredentialsRequest">credentials registration requests</see> and
		/// issues the credentials.
		/// </summary>
		/// <param name="registrationRequest">The request containing the credentials presentations, credential requests and the proofs.</param>
		/// <returns>The <see cref="CredentialsResponse">registration response</see> containing the issued credentials and the proofs.</returns>
		/// <exception cref="WabiSabiCryptoException">Error code: <see cref="WabiSabiCryptoErrorCode">WabiSabiErrorCode</see></exception>
		public CredentialsResponse HandleRequest(CredentialsRequest registrationRequest)
			=> PrepareResponse(registrationRequest).Commit();

		/// <summary>
		/// Validate the <see cref="CredentialsRequest">credentials registration requests</see> and
		/// prepare to mark the serial numbers as used and issue the credentials.
		/// </summary>
		/// <param name="registrationRequest">The request containing the credentials presentations, credential requests and the proofs.</param>
		/// <returns>The <see cref="ICommitableCredentialsResponse">The response ready to be committed to</see> which returns the <see cref="ICommitableCredentialsResponse">response</see> with the issued credentials and the proofs.</returns>
		/// <exception cref="WabiSabiCryptoException">Error code: <see cref="WabiSabiCryptoErrorCode">WabiSabiErrorCode</see></exception>
		public ICommitableCredentialsResponse PrepareResponse(CredentialsRequest registrationRequest)
		{
			Guard.NotNull(nameof(registrationRequest), registrationRequest);

			var requested = registrationRequest.Requested ?? Enumerable.Empty<IssuanceRequest>();
			var presented = registrationRequest.Presented ?? Enumerable.Empty<CredentialPresentation>();

			var requestedCount = requested.Count();
			if (requestedCount != NumberOfCredentials)
			{
				throw new WabiSabiCryptoException(
					WabiSabiCryptoErrorCode.InvalidNumberOfRequestedCredentials,
					$"{NumberOfCredentials} credential requests were expected but {requestedCount} were received.");
			}

			var presentedCount = presented.Count();
			var requiredNumberOfPresentations = registrationRequest.IsNullRequest ? 0 : NumberOfCredentials;
			if (presentedCount != requiredNumberOfPresentations)
			{
				throw new WabiSabiCryptoException(
					WabiSabiCryptoErrorCode.InvalidNumberOfPresentedCredentials,
					$"{requiredNumberOfPresentations} credential presentations were expected but {presentedCount} were received.");
			}

			// Don't allow balance to go negative. In case this goes below zero
			// then there is a problem somewhere because this should not be possible.
			if (Balance + registrationRequest.Delta < 0)
			{
				throw new InvalidOperationException("Negative issuer balance");
			}

			// Check that the range proofs are of the appropriate bitwidth
			var rangeProofWidth = registrationRequest.IsNullRequest ? 0 : RangeProofWidth;
			var allRangeProofsAreCorrectSize = requested.All(x => x.BitCommitments.Count() == rangeProofWidth);
			if (!allRangeProofsAreCorrectSize)
			{
				throw new WabiSabiCryptoException(WabiSabiCryptoErrorCode.InvalidBitCommitment);
			}

			// Check all the serial numbers are unique. Note that this is checked separately from
			// ensuring that they haven't been used before, because even presenting a previously
			// unused credential more than once in the same request is still a double spend.
			if (registrationRequest.AreThereDuplicatedSerialNumbers)
			{
				throw new WabiSabiCryptoException(WabiSabiCryptoErrorCode.SerialNumberDuplicated);
			}

			var presentedSerialNumbers = presented.Select(x => x.S);

			// Check if the serial numbers have been used before. Note that the
			// serial numbers have not yet been verified at this point, but a
			// request with an invalid proof and a used serial number should
			// also be rejected. Since serial numbers are cryptographically
			// unguessable, we just add them, any reuse is necessarily a double
			// spend attempt.
			var unusedSerialNumbers = presentedSerialNumbers.OrderBy(s => s.GetHashCode()).Where(s => SerialNumbers.TryAdd(s, true)).ToImmutableArray();

			if (unusedSerialNumbers.Length != presented.Count())
			{
				// Clean up any serial numbers that were unused prior to TryAdd,
				// since they may be invalid so otherwise they could be used to
				// clog the nullifier set. We only need to keep serial numbers
				// associated with valid MACs.
				foreach (var serial in unusedSerialNumbers)
				{
					SerialNumbers.Remove(serial, out _);
				}

				throw new WabiSabiCryptoException(WabiSabiCryptoErrorCode.SerialNumberAlreadyUsed, $"Serial number reused");
			}

			var statements = new List<Statement>();
			foreach (var presentation in presented)
			{
				// Calculate Z using coordinator secret.
				var z = presentation.ComputeZ(CredentialIssuerSecretKey);

				// Add the credential presentation to the statements to be verified.
				statements.Add(ProofSystem.ShowCredentialStatement(presentation, z, CredentialIssuerParameters));
			}

			foreach (var credentialRequest in requested)
			{
				statements.Add(registrationRequest.IsNullRequest
					? ProofSystem.ZeroProofStatement(credentialRequest.Ma)
					: ProofSystem.RangeProofStatement(credentialRequest.Ma, credentialRequest.BitCommitments, rangeProofWidth));
			}

			// Balance proof
			if (!registrationRequest.IsNullRequest)
			{
				var sumCa = presented.Select(x => x.Ca).Sum();
				var sumMa = requested.Select(x => x.Ma).Sum();

				// A positive Delta_a means the requested credential amounts are larger
				// than the presented ones (i.e. input registration, and a negative
				// balance correspond to output registration). The equation requires a
				// commitment to 0, so the sum of the presented attributes and the
				// negated requested attributes are tweaked by delta_a.
				var absAmountDelta = new Scalar((ulong)Math.Abs(registrationRequest.Delta));
				var deltaA = registrationRequest.Delta < 0 ? absAmountDelta.Negate() : absAmountDelta;
				var balanceTweak = deltaA * Generators.Gg;
				statements.Add(ProofSystem.BalanceProofStatement(balanceTweak + sumCa - sumMa));
			}

			var transcript = BuildTransnscript(registrationRequest.IsNullRequest);

			// Verify all statements.
			var areProofsValid = ProofSystem.Verify(transcript, statements, registrationRequest.Proofs);
			if (!areProofsValid)
			{
				// Request was invalid, but all serial numbers were unused.
				// Ensure nullifier set can't be clogged with invalid serial
				// numbers. Valid serial numbers are only issued in response to
				// valid requests, which in turn depend on valid ownership
				// proofs and the banning mechanism, and they actually matter
				// for double spending, so only they need to be stored in the
				// nullifier set.
				foreach (var serialNumber in presentedSerialNumbers)
				{
					SerialNumbers.Remove(serialNumber, out _);
				}

				throw new WabiSabiCryptoException(WabiSabiCryptoErrorCode.CoordinatorReceivedInvalidProofs);
			}

			// After this point serial numbers are committed irrevocably, even
			// if the request fails at a higher level, and the credentials
			// were never revealed because `Commit` was not called. This is
			// because any request that made it this far is formally valid, and
			// thus any invalidity with regard to state is unambiguously a
			// double spend attempt, and there is no point in allowing those
			// serial numbers to be reused and the round to proceed.

			// Issue the credentials, but they won't actually be returned until
			// Commit() is called.
			var credentials = requested.Select(x => IssueCredential(x.Ma, RandomNumberGenerator.GetScalar())).ToImmutableArray();

			// Construct response.
			var proofs = ProofSystem.Prove(transcript, credentials.Select(x => x.Knowledge), RandomNumberGenerator);
			var macs = credentials.Select(x => x.Mac);
			var response = new CredentialsResponse(macs, proofs);

			return new PreparedCredentialsResponse(this, response, registrationRequest.Delta);
		}

		private CredentialsResponse Commit(CredentialsResponse response, long delta)
		{
			Interlocked.Add(ref _balance, delta);

			if (Balance < 0)
			{
				throw new InvalidOperationException("Negative balance");
			}

			// Although there are no side effects, eagerly evaluate enumerables
			// to ensure the expensive computations are not repeated.
			return new(
				response.IssuedCredentials.ToImmutableArray(),
				response.Proofs.ToImmutableArray()
			);
		}

		private (MAC Mac, Knowledge Knowledge) IssueCredential(GroupElement ma, Scalar t)
		{
			var sk = CredentialIssuerSecretKey;
			var mac = MAC.ComputeMAC(sk, ma, t);
			var knowledge = ProofSystem.IssuerParametersKnowledge(mac, ma, sk);
			return (mac, knowledge);
		}

		private Transcript BuildTransnscript(bool isNullRequest)
		{
			var label = $"UnifiedRegistration/{NumberOfCredentials}/{isNullRequest}";
			var encodedLabel = Encoding.UTF8.GetBytes(label);
			return new Transcript(encodedLabel);
		}

		private class PreparedCredentialsResponse : ICommitableCredentialsResponse
		{
			private readonly CredentialIssuer _issuer;
			private readonly CredentialsResponse _response;
			private readonly long _delta;
			private bool _committed;

			public PreparedCredentialsResponse(CredentialIssuer issuer, CredentialsResponse response, long delta)
			{
				_issuer = issuer;
				_response = response;
				_delta = delta;
				_committed = false;
			}

			public CredentialsResponse Commit()
			{
				if (_committed)
				{
					throw new InvalidOperationException("The instance was already committed.");
				}
				_committed = true;
				return _issuer.Commit(_response, _delta);
			}
		}
	}
}

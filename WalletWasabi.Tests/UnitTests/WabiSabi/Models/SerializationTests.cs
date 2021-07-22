using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBitcoin.Secp256k1;
using Newtonsoft.Json;
using WalletWasabi.Crypto;
using WalletWasabi.Crypto.Groups;
using WalletWasabi.Crypto.Randomness;
using WalletWasabi.Crypto.ZeroKnowledge;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi.Crypto;
using WalletWasabi.WabiSabi.Crypto.CredentialRequesting;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;
using WalletWasabi.WabiSabi.Models.Serialization;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Models
{
	public class SerializationTests
	{
		private static IEnumerable<GroupElement> Points = Enumerable.Range(0, int.MaxValue).Select(i => Generators.FromText($"T{i}"));
		private static IEnumerable<Scalar> Scalars = Enumerable.Range(1, int.MaxValue).Select(i => new Scalar((uint)i));
		private static CredentialIssuerSecretKey IssuerKey = new(new InsecureRandom());

		[Fact]
		public void InputRegistrationRequestMessageSerialization()
		{
			var message = new InputRegistrationRequest(
					BitcoinFactory.CreateUint256(),
					BitcoinFactory.CreateOutPoint(),
					new OwnershipProof(),
					CreateZeroCredentialsRequest(),
					CreateZeroCredentialsRequest());

			AssertSerialization(message);
		}

		[Fact]
		public void InputRegistrationResponseMessageSerialization()
		{
			var message = new InputRegistrationResponse(
				BitcoinFactory.CreateUint256(),
				CreateCredentialsResponse(),
				CreateCredentialsResponse());

			AssertSerialization(message);
		}

		[Fact]
		public void ConnectionConfirmationRequestMessageSerialization()
		{
			var message = new ConnectionConfirmationRequest(
				BitcoinFactory.CreateUint256(),
				BitcoinFactory.CreateUint256(),
				CreateZeroCredentialsRequest(),
				CreateRealCredentialsRequest(),
				CreateZeroCredentialsRequest(),
				CreateRealCredentialsRequest());

			AssertSerialization(message);
		}

		[Fact]
		public void ConnectionConfirmationResponseMessageSerialization()
		{
			var message = new ConnectionConfirmationResponse(
				CreateCredentialsResponse(),
				CreateCredentialsResponse(),
				CreateCredentialsResponse(),
				CreateCredentialsResponse());

			AssertSerialization(message);
		}

		[Fact]
		public void OutputRegistrationRequestMessageSerialization()
		{
			var message = new OutputRegistrationRequest(
				BitcoinFactory.CreateUint256(),
				BitcoinFactory.CreateScript(),
				CreateRealCredentialsRequest(),
				CreateRealCredentialsRequest());

			AssertSerialization(message);
		}

		[Fact]
		public void ReissueCredentialRequestMessageSerialization()
		{
			var message = new ReissueCredentialRequest(
				BitcoinFactory.CreateUint256(),
				CreateRealCredentialsRequest(),
				CreateRealCredentialsRequest(),
				CreateZeroCredentialsRequest(),
				CreateZeroCredentialsRequest());

			AssertSerialization(message);
		}

		[Fact]
		public void ReissueCredentialResponseMessageSerialization()
		{
			var message = new ReissueCredentialResponse(
				CreateCredentialsResponse(),
				CreateCredentialsResponse(),
				CreateCredentialsResponse(),
				CreateCredentialsResponse());

			AssertSerialization(message);
		}

		[Fact]
		public void InpuRemovalRequestMessageSerialization()
		{
			var message = new InputsRemovalRequest(
				BitcoinFactory.CreateUint256(),
				BitcoinFactory.CreateUint256());

			AssertSerialization(message);
		}

		[Fact]
		public void TransactionSignatureRequestMessageSerialization()
		{
			using var key1 = new Key();
			using var key2 = new Key();
			var message = new TransactionSignaturesRequest(
				BitcoinFactory.CreateUint256(),
				new[]
				{
					new InputWitnessPair(1, new WitScript(Op.GetPushOp(key1.PubKey.ToBytes())) ),
					new InputWitnessPair(17, new WitScript(Op.GetPushOp(key2.PubKey.ToBytes())) )
				});

			AssertSerialization(message);
		}

		[Fact]
		public void RoundStateMessageSerialization()
		{
			var round = WabiSabiFactory.CreateRound(new WalletWasabi.WabiSabi.Backend.WabiSabiConfig());
			AssertSerialization(RoundState.FromRound(round));

			var state = round.Assert<ConstructionState>();
			round.CoinjoinState = new SigningState(state.Parameters, state.Inputs, state.Outputs);
			AssertSerialization(RoundState.FromRound(round));
		}

		private static void AssertSerialization<T>(T message)
		{
			var serializedMessage = JsonConvert.SerializeObject(message, JsonSerializationOptions.Default.Settings);
			var deserializedMessage = JsonConvert.DeserializeObject<T>(serializedMessage, JsonSerializationOptions.Default.Settings);
			var reserializedMessage = JsonConvert.SerializeObject(deserializedMessage, JsonSerializationOptions.Default.Settings);

			Assert.Equal(reserializedMessage, serializedMessage);
		}

		private static ZeroCredentialsRequest CreateZeroCredentialsRequest() =>
			new(
				new[] { new IssuanceRequest(Points.First(), Points.Take(2)) },
				new[] { new Proof(new GroupElementVector(Points.Take(2)), new ScalarVector(Scalars.Take(2))) });

		private static RealCredentialsRequest CreateRealCredentialsRequest() =>
			new(
				123456L,
				new[] { new CredentialPresentation(Points.First(), Points.First(), Points.First(), Points.First(), Points.First()) },
				new[] { new IssuanceRequest(Points.First(), Points.Take(2)) },
				new[] { new Proof(new GroupElementVector(Points.Take(2)), new ScalarVector(Scalars.Take(2))) });

		private static CredentialsResponse CreateCredentialsResponse() =>
			new(
				new[] { MAC.ComputeMAC(IssuerKey, Points.First(), Scalars.First()) },
				new[] { new Proof(new GroupElementVector(Points.Take(2)), new ScalarVector(Scalars.Take(2))) });
	}
}

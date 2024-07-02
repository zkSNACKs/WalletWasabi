using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NBitcoin;
using NBitcoin.Secp256k1;
using Newtonsoft.Json;
using WabiSabi;
using WabiSabi.CredentialRequesting;
using WabiSabi.Crypto;
using WabiSabi.Crypto.Groups;
using WabiSabi.Crypto.ZeroKnowledge;
using WalletWasabi.Crypto;
using WalletWasabi.Crypto.Randomness;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;
using WalletWasabi.WabiSabi.Models.Serialization;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Models;

public class SerializationTests
{
	private static IEnumerable<GroupElement> Points = Enumerable.Range(0, int.MaxValue).Select(i => Generators.FromText($"T{i}"));
	private static IEnumerable<Scalar> Scalars = Enumerable.Range(1, int.MaxValue).Select(i => new Scalar((uint)i));
	private static CredentialIssuerSecretKey IssuerKey = new(InsecureRandom.Instance);

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
			Guid.NewGuid(),
			CreateCredentialsResponse(),
			CreateCredentialsResponse());

		AssertSerialization(message);
	}

	[Fact]
	public void ConnectionConfirmationRequestMessageSerialization()
	{
		var message = new ConnectionConfirmationRequest(
			BitcoinFactory.CreateUint256(),
			Guid.NewGuid(),
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
			Guid.NewGuid());

		AssertSerialization(message);
	}

	[Fact]
	public void TransactionSignatureRequestMessageSerialization()
	{
		using var key1 = new Key();
		using var key2 = new Key();
		var message = new TransactionSignaturesRequest(BitcoinFactory.CreateUint256(), 1, new WitScript(Op.GetPushOp(key1.PubKey.ToBytes())));

		AssertSerialization(message);
	}

	[Fact]
	public void RoundStateMessageSerialization()
	{
		var round = WabiSabiFactory.CreateRound(new WalletWasabi.WabiSabi.Backend.WabiSabiConfig());
		AssertSerialization(RoundState.FromRound(round));

		var state = round.Assert<ConstructionState>();
		(var coin, var ownershipProof) = WabiSabiFactory.CreateCoinWithOwnershipProof(roundId: round.Id);
		state = state.AddInput(coin, ownershipProof, WabiSabiFactory.CreateCommitmentData(round.Id));
		round.CoinjoinState = new SigningState(state.Parameters, state.Events);
		AssertSerialization(RoundState.FromRound(round));
	}

	[Fact]
	public void CoinSerialization()
	{
		var coin = new Coin(
			new OutPoint(
				uint256.One,
				1234),
			new TxOut(
				Money.Coins(1),
				new Script("0 bf3593d140d512eb607b3ddb5c5ee085f1e3a210")));
		AssertSerialization(coin);

		var serializedCoin = JsonConvert.SerializeObject(coin, JsonSerializationOptions.Default.Settings);
		var expectedJson = "{\"Outpoint\":\"0100000000000000000000000000000000000000000000000000000000000000D2040000\",\"TxOut\":{\"ScriptPubKey\":\"0 bf3593d140d512eb607b3ddb5c5ee085f1e3a210\",\"Value\":100000000}}";
		Assert.Equal(expectedJson, serializedCoin);
	}

	[Fact]
	public void RoundStateRequestSerialization()
	{
		RoundStateCheckpoint stateCheckpoint = new(uint256.One, 0);
		RoundStateRequest request = new(ImmutableList.Create(stateCheckpoint));

		AssertSerialization(request);
	}

	[Fact]
	public void RoundStateResponseSerialization()
	{
		var round = WabiSabiFactory.CreateRound(new WalletWasabi.WabiSabi.Backend.WabiSabiConfig());
		var roundState = RoundState.FromRound(round);
		CoinJoinFeeRateMedian median = new(TimeSpan.FromHours(24), new FeeRate(120m));
		RoundStateResponse response = new(new[] { roundState }, new[] { median });

		AssertSerialization(response);
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

using Moq;
using NBitcoin;
using NBitcoin.RPC;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend.Controllers;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Crypto;
using WalletWasabi.Crypto.Randomness;
using WalletWasabi.Crypto.ZeroKnowledge;
using WalletWasabi.Helpers;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi;
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.Banning;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WabiSabi.Crypto;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Client
{
	public class ArenaClientTests
	{
		[Fact]
		public async Task FullCoinjoinAsyncTestAsync()
		{
			var config = new WabiSabiConfig { MaxInputCountByRound = 1 };
			var round = WabiSabiFactory.CreateRound(config);
			using var key = new Key();
			var outpoint = BitcoinFactory.CreateOutPoint();
			var mockRpc = new Mock<IRPCClient>();
			mockRpc.Setup(rpc => rpc.GetTxOutAsync(outpoint.Hash, (int)outpoint.N, true))
				.ReturnsAsync(new NBitcoin.RPC.GetTxOutResponse
				{
					IsCoinBase = false,
					Confirmations = 200,
					TxOut = new TxOut(Money.Coins(1m), key.PubKey.WitHash.GetAddress(Network.Main)),
				});
			mockRpc.Setup(rpc => rpc.EstimateSmartFeeAsync(It.IsAny<int>(), It.IsAny<EstimateSmartFeeMode>()))
				.ReturnsAsync(new EstimateSmartFeeResponse
				{
					Blocks = 1000,
					FeeRate = new FeeRate(10m)
				});
			mockRpc.Setup(rpc => rpc.GetMempoolInfoAsync(It.IsAny<CancellationToken>()))
				.ReturnsAsync(new MemPoolInfo
				{
					MinRelayTxFee = 1
				});

			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(config, mockRpc, round);
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromMinutes(1));

			await using var coordinator = new ArenaRequestHandler(config, new Prison(), arena, mockRpc.Object);
			var wabiSabiApi = new WabiSabiController(coordinator);
			var insecureRandom = new InsecureRandom();
			var roundState = RoundState.FromRound(round);
			var aliceArenaClient = new ArenaClient(
				roundState.CreateAmountCredentialClient(insecureRandom),
				roundState.CreateVsizeCredentialClient(insecureRandom),
				wabiSabiApi);

			var inputRegistrationResponse = await aliceArenaClient.RegisterInputAsync(round.Id, outpoint, key, CancellationToken.None);
			var aliceId = inputRegistrationResponse.Value;

			var inputVsize = Constants.P2wpkhInputVirtualSize;
			var amountsToRequest = new[]
			{
				Money.Coins(.75m) - round.FeeRate.GetFee(inputVsize),
				Money.Coins(.25m),
			}.Select(x => x.Satoshi).ToArray();

			using var destinationKey1 = new Key();
			using var destinationKey2 = new Key();
			var p2wpkhScriptSize = (long)destinationKey1.PubKey.WitHash.ScriptPubKey.EstimateOutputVsize();

			var vsizesToRequest = new[] { roundState.MaxVsizeAllocationPerAlice - ( inputVsize + 2 * p2wpkhScriptSize ), 2 * p2wpkhScriptSize };

			// Phase: Input Registration
			Assert.Equal(Phase.InputRegistration, round.Phase);

			var connectionConfirmationResponse1 = await aliceArenaClient.ConfirmConnectionAsync(
				round.Id,
				aliceId,
				amountsToRequest,
				vsizesToRequest,
				inputRegistrationResponse.IssuedAmountCredentials,
				inputRegistrationResponse.IssuedVsizeCredentials,
				CancellationToken.None);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromMinutes(1));
			Assert.Equal(Phase.ConnectionConfirmation, round.Phase);

			// Phase: Connection Confirmation
			var connectionConfirmationResponse2 = await aliceArenaClient.ConfirmConnectionAsync(
				round.Id,
				aliceId,
				amountsToRequest,
				vsizesToRequest,
				connectionConfirmationResponse1.IssuedAmountCredentials,
				connectionConfirmationResponse1.IssuedVsizeCredentials,
				CancellationToken.None);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromSeconds(1));

			// Phase: Output Registration
			Assert.Equal(Phase.OutputRegistration, round.Phase);

			var bobArenaClient = new ArenaClient(
				roundState.CreateAmountCredentialClient(insecureRandom),
				roundState.CreateVsizeCredentialClient(insecureRandom),
				wabiSabiApi);

			var reissuanceResponse = await bobArenaClient.ReissueCredentialAsync(
				round.Id,
				amountsToRequest,
				Enumerable.Repeat(p2wpkhScriptSize, 2),
				connectionConfirmationResponse2.IssuedAmountCredentials.Take(ProtocolConstants.CredentialNumber),
				connectionConfirmationResponse2.IssuedVsizeCredentials.Skip(1).Take(ProtocolConstants.CredentialNumber), // first amount is the leftover value
				CancellationToken.None);

			Credential amountCred1 = reissuanceResponse.IssuedAmountCredentials.ElementAt(0);
			Credential amountCred2 = reissuanceResponse.IssuedAmountCredentials.ElementAt(1);
			Credential zeroAmountCred1 = reissuanceResponse.IssuedAmountCredentials.ElementAt(2);
			Credential zeroAmountCred2 = reissuanceResponse.IssuedAmountCredentials.ElementAt(3);

			Credential vsizeCred1 = reissuanceResponse.IssuedVsizeCredentials.ElementAt(0);
			Credential vsizeCred2 = reissuanceResponse.IssuedVsizeCredentials.ElementAt(1);
			Credential zeroVsizeCred1 = reissuanceResponse.IssuedVsizeCredentials.ElementAt(2);
			Credential zeroVsizeCred2 = reissuanceResponse.IssuedVsizeCredentials.ElementAt(3);

			await bobArenaClient.RegisterOutputAsync(
				round.Id,
				amountsToRequest[0],
				destinationKey1.PubKey.WitHash.ScriptPubKey,
				new[] { amountCred1, zeroAmountCred1 },
				new[] { vsizeCred1, zeroVsizeCred1 },
				CancellationToken.None);

			await bobArenaClient.RegisterOutputAsync(
				round.Id,
				amountsToRequest[1],
				destinationKey2.PubKey.WitHash.ScriptPubKey,
				new[] { amountCred2, zeroAmountCred2 },
				new[] { vsizeCred2, zeroVsizeCred2 },
				CancellationToken.None);

			await aliceArenaClient.ReadyToSignAsync(round.Id, aliceId, key, CancellationToken.None);

			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromMinutes(1));
			Assert.Equal(Phase.TransactionSigning, round.Phase);

			var tx = round.Assert<SigningState>().CreateTransaction();
			Assert.Single(tx.Inputs);
			Assert.Equal(2, tx.Outputs.Count);
		}

		[Fact]
		public async Task RemoveInputAsyncTestAsync()
		{
			var config = new WabiSabiConfig();
			var round = WabiSabiFactory.CreateRound(config);
			round.SetPhase(Phase.ConnectionConfirmation);
			var fundingTx = BitcoinFactory.CreateSmartTransaction(ownOutputCount: 1);
			var coin = fundingTx.WalletOutputs.First().Coin;
			var alice = new Alice(coin, new OwnershipProof(), round);
			round.Alices.Add(alice);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(config, round);

			await using var coordinator = new ArenaRequestHandler(config, new Prison(), arena, arena.Rpc);
			var wabiSabiApi = new WabiSabiController(coordinator);
			var apiClient = new ArenaClient(null!, null!, wabiSabiApi);

			round.SetPhase(Phase.InputRegistration);

			await apiClient.RemoveInputAsync(round.Id, alice.Id, CancellationToken.None);
			Assert.Empty(round.Alices);
		}

		[Fact]
		public async Task SignTransactionAsync()
		{
			WabiSabiConfig config = new();
			Round round = WabiSabiFactory.CreateRound(config);

			using Key key1 = new();
			Alice alice1 = WabiSabiFactory.CreateAlice(key: key1, round: round);
			round.Alices.Add(alice1);

			using Key key2 = new();
			Alice alice2 = WabiSabiFactory.CreateAlice(key: key2, round: round);
			round.Alices.Add(alice2);

			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(config, round);

			var mockRpc = new Mock<IRPCClient>();
			await using var coordinator = new ArenaRequestHandler(config, new Prison(), arena, mockRpc.Object);
			var wabiSabiApi = new WabiSabiController(coordinator);

			var rnd = new InsecureRandom();
			var amountClient = new WabiSabiClient(round.AmountCredentialIssuerParameters, rnd, 4300000000000L);
			var vsizeClient = new WabiSabiClient(round.VsizeCredentialIssuerParameters, rnd, 2000L);
			var apiClient = new ArenaClient(amountClient, vsizeClient, wabiSabiApi);

			round.SetPhase(Phase.TransactionSigning);

			var emptyState = round.Assert<ConstructionState>();

			// We can't use ``emptyState.Finalize()` because this is not a valid transaction so we fake it
			var finalizedEmptyState = new SigningState(emptyState.Parameters, emptyState.Inputs, emptyState.Outputs);

			// No inputs in the CoinJoin.
			await Assert.ThrowsAsync<ArgumentException>(async () =>
				await apiClient.SignTransactionAsync(round.Id, alice1.Coin, new BitcoinSecret(key1, Network.Main), finalizedEmptyState.CreateUnsignedTransaction(), CancellationToken.None));

			var oneInput = emptyState.AddInput(alice1.Coin).Finalize();
			round.CoinjoinState = oneInput;

			// Trying to sign coins those are not in the CoinJoin.
			await Assert.ThrowsAsync<InvalidOperationException>(async () =>
				await apiClient.SignTransactionAsync(round.Id, alice2.Coin, new BitcoinSecret(key2, Network.Main), oneInput.CreateUnsignedTransaction(), CancellationToken.None));

			var twoInputs = emptyState.AddInput(alice1.Coin).AddInput(alice2.Coin).Finalize();
			round.CoinjoinState = twoInputs;

			// Trying to sign coins with the wrong secret.
			await Assert.ThrowsAsync<InvalidOperationException>(async () =>
				await apiClient.SignTransactionAsync(round.Id, alice1.Coin, new BitcoinSecret(key2, Network.Main), twoInputs.CreateUnsignedTransaction(), CancellationToken.None));

			Assert.False(round.Assert<SigningState>().IsFullySigned);

			var unsigned = round.Assert<SigningState>().CreateUnsignedTransaction();

			await apiClient.SignTransactionAsync(round.Id, alice1.Coin, new BitcoinSecret(key1, Network.Main), unsigned, CancellationToken.None);
			Assert.True(round.Assert<SigningState>().IsInputSigned(alice1.Coin.Outpoint));
			Assert.False(round.Assert<SigningState>().IsInputSigned(alice2.Coin.Outpoint));

			Assert.False(round.Assert<SigningState>().IsFullySigned);

			await apiClient.SignTransactionAsync(round.Id, alice2.Coin, new BitcoinSecret(key2, Network.Main), unsigned, CancellationToken.None);
			Assert.True(round.Assert<SigningState>().IsInputSigned(alice2.Coin.Outpoint));

			Assert.True(round.Assert<SigningState>().IsFullySigned);
		}
	}
}

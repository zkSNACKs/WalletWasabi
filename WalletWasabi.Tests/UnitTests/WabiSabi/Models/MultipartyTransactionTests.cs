using NBitcoin;
using System.Collections.Immutable;
using System.Linq;
using WalletWasabi.Crypto;
using WalletWasabi.Extensions;
using WalletWasabi.Helpers;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Models;

public class MultipartyTransactionTests
{
	private static readonly MoneyRange DefaultAllowedAmounts = new(Money.Zero, Money.Coins(1));

	private static readonly RoundParameters DefaultParameters = WabiSabiFactory.CreateRoundParameters(new()
	{
		MinRegistrableAmount = DefaultAllowedAmounts.Min,
		MaxRegistrableAmount = DefaultAllowedAmounts.Max,
		MaxSuggestedAmountBase = Money.Coins(Constants.MaximumNumberOfBitcoins)
	}) with
	{
		MiningFeeRate = new FeeRate(0m)
	};

	private static readonly CoinJoinInputCommitmentData CommitmentData = WabiSabiFactory.CreateCommitmentData();

	private static void ThrowsProtocolException(WabiSabiProtocolErrorCode expectedError, Action action) =>
		Assert.Equal(expectedError, Assert.Throws<WabiSabiProtocolException>(action).ErrorCode);

	[Fact]
	public void TwoPartiesNoFees()
	{
		using Key key1 = new();
		using Key key2 = new();

		(var alice1Coin, var alice1OwnershipProof) = WabiSabiFactory.CreateCoinWithOwnershipProof(key1);
		(var alice2Coin, var alice2OwnershipProof) = WabiSabiFactory.CreateCoinWithOwnershipProof(key2);

		var state = new ConstructionState(DefaultParameters);

		Assert.Empty(state.Inputs);
		Assert.Empty(state.Outputs);

		var oneInput = state.AddInput(alice1Coin, alice1OwnershipProof, CommitmentData);

		Assert.Single(oneInput.Inputs);
		Assert.Empty(oneInput.Outputs);

		// Previous state should be unmodified
		Assert.Empty(state.Inputs);
		Assert.Empty(state.Outputs);

		var differentInput = state.AddInput(alice2Coin, alice2OwnershipProof, CommitmentData);

		Assert.Single(differentInput.Inputs);
		Assert.Empty(differentInput.Outputs);
		Assert.NotEqual(oneInput.Inputs, differentInput.Inputs);
		Assert.Equal(oneInput.Outputs, differentInput.Outputs);

		var twoInputs = oneInput.AddInput(alice2Coin, alice2OwnershipProof, CommitmentData);

		Assert.Equal(2, twoInputs.Inputs.Count());
		Assert.Empty(twoInputs.Outputs);

		// address reuse bad
		var bob1 = new TxOut(Money.Coins(1), alice1Coin.ScriptPubKey);
		var withOutput = twoInputs.AddOutput(bob1);

		Assert.Equal(2, withOutput.Inputs.Count());
		Assert.Single(withOutput.Outputs);

		var bob2 = new TxOut(Money.Coins(1), alice2Coin.ScriptPubKey);
		var noFeeTx = withOutput.AddOutput(bob2).Finalize();
		Assert.Equal(Money.Zero, noFeeTx.Balance);

		var tx = noFeeTx.CreateUnsignedTransaction();
		Assert.Equal(2, tx.Inputs.Count);
		Assert.Equal(2, tx.Outputs.Count);
		Assert.Contains(alice1Coin.Outpoint, tx.Inputs.Select(x => x.PrevOut));
		Assert.Contains(alice2Coin.Outpoint, tx.Inputs.Select(x => x.PrevOut));
		Assert.Contains(alice1Coin.ScriptPubKey, tx.Outputs.Select(x => x.ScriptPubKey));
		Assert.Contains(alice2Coin.ScriptPubKey, tx.Outputs.Select(x => x.ScriptPubKey));

		var alice1Tx = tx.Clone();
		alice1Tx.Sign(key1.GetBitcoinSecret(Network.Main), alice1Coin);

		var alice1SignedInput = alice1Tx.Inputs.Select((x, i) => (Input: x, Index: i)).Single(x => x.Input.HasWitScript());
		var alice1Sig = noFeeTx.AddWitness(alice1SignedInput.Index, alice1SignedInput.Input.WitScript);
		Assert.True(alice1Sig.IsInputSigned(alice1SignedInput.Index));
		Assert.False(alice1Sig.IsInputSigned(alice1SignedInput.Index ^ 1));
		Assert.False(alice1Sig.IsFullySigned);
		Assert.Equal(alice1Tx.ToString(), alice1Sig.CreateTransaction().ToString());

		var alice2Tx = tx.Clone();
		alice2Tx.Sign(key2.GetBitcoinSecret(Network.Main), alice2Coin);

		var alice2SignedInput = alice2Tx.Inputs.Select((x, i) => (Input: x, Index: i)).Single(x => x.Input.HasWitScript());
		var alice2Sig = alice1Sig.AddWitness(alice2SignedInput.Index, alice2SignedInput.Input.WitScript);
		Assert.True(alice2Sig.IsInputSigned(alice2SignedInput.Index));
		Assert.True(alice2Sig.IsInputSigned(alice2SignedInput.Index ^ 1));
		Assert.True(alice2Sig.IsFullySigned);

		var signed = alice2Sig.CreateTransaction();
		Assert.NotEqual(alice1Tx.ToString(), signed.ToString());
		Assert.NotEqual(alice2Tx.ToString(), signed.ToString());
		Assert.True(signed.Inputs.All(x => x.HasWitScript()));
	}

	[Fact]
	public void AddWithOptimize()
	{
		(var coin, var ownershipProof) = WabiSabiFactory.CreateCoinWithOwnershipProof();

		var state = new ConstructionState(DefaultParameters).AddInput(coin, ownershipProof, CommitmentData);

		var script = BitcoinFactory.CreateScript();
		var bob = new TxOut(coin.Amount / 2, script);
		var withOutput = state.AddOutput(bob);
		var duplicateOutputNoFee = withOutput.AddOutput(bob).Finalize();

		var tx2 = duplicateOutputNoFee.CreateUnsignedTransaction();
		var output = Assert.Single(tx2.Outputs);
		Assert.Equal(script, output.ScriptPubKey);
		Assert.Equal(coin.Amount, output.Value);
	}

	[Fact]
	public void WitnessValidation()
	{
		using Key key1 = new();
		using Key key2 = new();

		(var alice1Coin, var alice1OwnershipProof) = WabiSabiFactory.CreateCoinWithOwnershipProof(key1);
		(var alice2Coin, var alice2OwnershipProof) = WabiSabiFactory.CreateCoinWithOwnershipProof(key2);

		var state = new ConstructionState(DefaultParameters).AddInput(alice1Coin, alice1OwnershipProof, CommitmentData).AddInput(alice2Coin, alice2OwnershipProof, CommitmentData);

		// address reuse bad
		var bob1 = new TxOut(Money.Coins(1), alice1Coin.ScriptPubKey);
		var withOutput = state.AddOutput(bob1);

		var bob2 = new TxOut(Money.Coins(1), alice2Coin.ScriptPubKey);
		var noFeeTx = withOutput.AddOutput(bob2).Finalize();

		var tx = noFeeTx.CreateUnsignedTransaction();

		var alice1Tx = tx.Clone();
		alice1Tx.Sign(key1.GetBitcoinSecret(Network.Main), alice1Coin);
		var alice2Tx = tx.Clone();
		alice2Tx.Sign(key2.GetBitcoinSecret(Network.Main), alice2Coin);

		var alice1SignedInput = alice1Tx.Inputs.Select((x, i) => (Input: x, Index: i)).Single(x => x.Input.HasWitScript());
		var alice2SignedInput = alice2Tx.Inputs.Select((x, i) => (Input: x, Index: i)).Single(x => x.Input.HasWitScript());

		var validSignatures = new[] { alice1SignedInput, alice2SignedInput };

		var invalidSignatures = Enumerable.Concat(alice1Tx.Inputs, alice2Tx.Inputs)
			.SelectMany(x => Enumerable.Range(0, 2), (input, idx) => (Input: input, Index: idx))
			.Except(validSignatures);

		// Only accept valid witnesses
		foreach (var invalidSignature in invalidSignatures)
		{
			ThrowsProtocolException(WabiSabiProtocolErrorCode.WrongCoinjoinSignature, () => noFeeTx.AddWitness(invalidSignature.Index, invalidSignature.Input.WitScript));
		}

		// Add Alice 1's signature
		var alice1Sig = noFeeTx.AddWitness(alice1SignedInput.Index, alice1SignedInput.Input.WitScript);
		Assert.False(alice1Sig.IsFullySigned);

		// Add Alice 2's signature
		var alice2Sig = alice1Sig.AddWitness(alice2SignedInput.Index, alice2SignedInput.Input.WitScript);
		Assert.True(alice2Sig.IsFullySigned);

		// Witness can only be accepted once per input
		ThrowsProtocolException(WabiSabiProtocolErrorCode.WitnessAlreadyProvided, () => alice1Sig.AddWitness(alice1SignedInput.Index, alice1SignedInput.Input.WitScript));
	}
	[Fact]
	public void PublishWitnessesTest()
	{
		using Key key1 = new();
		using Key key2 = new();

		(var alice1Coin, var alice1OwnershipProof) = WabiSabiFactory.CreateCoinWithOwnershipProof(key1);
		(var alice2Coin, var alice2OwnershipProof) = WabiSabiFactory.CreateCoinWithOwnershipProof(key2);
		var bob1 = new TxOut(Money.Coins(1), alice1Coin.ScriptPubKey);
		var bob2 = new TxOut(Money.Coins(1), alice2Coin.ScriptPubKey);

		var noFeeTx = new ConstructionState(DefaultParameters)
			.AddInput(alice1Coin, alice1OwnershipProof, CommitmentData)
			.AddInput(alice2Coin, alice2OwnershipProof, CommitmentData)
			.AddOutput(bob1)
			.AddOutput(bob2)
			.Finalize();

		Assert.Empty(noFeeTx.Witnesses);

		var tx = noFeeTx.CreateUnsignedTransaction();
		var alice1Tx = tx.Clone();
		alice1Tx.Sign(key1.GetBitcoinSecret(Network.Main), alice1Coin);

		var alice1SignedInput = alice1Tx.Inputs.Select((x, i) => (Input: x, Index: i)).Single(x => x.Input.HasWitScript());
		var alice1Sig = noFeeTx.AddWitness(alice1SignedInput.Index, alice1SignedInput.Input.WitScript);

		// First part: Publish witnesses one by one
		Assert.Empty(alice1Sig.Witnesses);
		Assert.False(alice1Sig.IsFullySigned);

		// Publish first witness
		var alice1SigPub = alice1Sig.PublishWitnesses();
		Assert.Single(alice1SigPub.Witnesses);
		Assert.False(alice1SigPub.IsFullySigned);

		var alice2Tx = tx.Clone();
		alice2Tx.Sign(key2.GetBitcoinSecret(Network.Main), alice2Coin);
		var alice2SignedInput = alice2Tx.Inputs.Select((x, i) => (Input: x, Index: i)).Single(x => x.Input.HasWitScript());
		var alice2Sig = alice1SigPub.AddWitness(alice2SignedInput.Index, alice2SignedInput.Input.WitScript);
		Assert.Single(alice2Sig.Witnesses);
		Assert.True(alice2Sig.IsFullySigned);

		//Publish second witness
		var alice2SigPub = alice2Sig.PublishWitnesses();
		Assert.Equal(2, alice2SigPub.Witnesses.Count);
		Assert.True(alice2SigPub.IsFullySigned);

		// Second part: Publish two witnesses at once
		var alice3Sig = alice1Sig.AddWitness(alice2SignedInput.Index, alice2SignedInput.Input.WitScript);
		Assert.Empty(alice3Sig.Witnesses);
		Assert.True(alice3Sig.IsFullySigned);

		// Publish both witnesses
		var alice3SigPub = alice3Sig.PublishWitnesses();
		Assert.Equal(2, alice3SigPub.Witnesses.Count);
		Assert.True(alice3SigPub.IsFullySigned);

		var signed = alice3SigPub.CreateTransaction();
		Assert.True(signed.Inputs.All(x => x.HasWitScript()));
	}

	[Fact]
	public void FeeRateValidation()
	{
		var feeRate = new FeeRate(new Money(1000L));

		using Key key1 = new();
		using Key key2 = new();

		(var alice1Coin, var alice1OwnershipProof) = WabiSabiFactory.CreateCoinWithOwnershipProof(key1);
		(var alice2Coin, var alice2OwnershipProof) = WabiSabiFactory.CreateCoinWithOwnershipProof(key2);

		var state = new ConstructionState(DefaultParameters with { MiningFeeRate = feeRate })
			.AddInput(alice1Coin, alice1OwnershipProof, CommitmentData)
			.AddInput(alice2Coin, alice2OwnershipProof, CommitmentData);

		var bob1 = new TxOut(Money.Coins(1), alice1Coin.ScriptPubKey);
		var withOutput = state.AddOutput(bob1);

		Assert.Equal(2, withOutput.Inputs.Count());
		Assert.Single(withOutput.Outputs);

		var bob2 = new TxOut(Money.Coins(1), alice2Coin.ScriptPubKey);
		ThrowsProtocolException(WabiSabiProtocolErrorCode.InsufficientFees, () => withOutput.AddOutput(bob2).Finalize());

		bob2 = new TxOut(Money.Coins(0.9999m), alice2Coin.ScriptPubKey);
		var generousFeeTx = withOutput.AddOutput(bob2).Finalize();

		var tx = generousFeeTx.CreateUnsignedTransaction();

		Assert.Contains(alice1Coin.Outpoint, tx.Inputs.Select(x => x.PrevOut));
		Assert.Contains(alice2Coin.Outpoint, tx.Inputs.Select(x => x.PrevOut));

		Assert.Contains(alice1Coin.ScriptPubKey, tx.Outputs.Select(x => x.ScriptPubKey));
		Assert.Contains(alice2Coin.ScriptPubKey, tx.Outputs.Select(x => x.ScriptPubKey));

		var alice1Tx = tx.Clone();
		alice1Tx.Sign(key1.GetBitcoinSecret(Network.Main), alice1Coin);

		var alice1SignedInput = alice1Tx.Inputs.Select((x, i) => (Input: x, Index: i)).Single(x => x.Input.HasWitScript());
		var alice1Sig = generousFeeTx.AddWitness(alice1SignedInput.Index, alice1SignedInput.Input.WitScript);
		Assert.False(alice1Sig.IsFullySigned);
		Assert.Equal(alice1Tx.ToString(), alice1Sig.CreateTransaction().ToString());

		var alice2Tx = tx.Clone();
		alice2Tx.Sign(key2.GetBitcoinSecret(Network.Main), alice2Coin);

		var alice2SignedInput = alice2Tx.Inputs.Select((x, i) => (Input: x, Index: i)).Single(x => x.Input.HasWitScript());
		var alice2Sig = alice1Sig.AddWitness(alice2SignedInput.Index, alice2SignedInput.Input.WitScript);
		Assert.True(alice2Sig.IsFullySigned);

		var signed = alice2Sig.CreateTransaction();
		Assert.NotEqual(alice1Tx.ToString(), signed.ToString());
		Assert.NotEqual(alice2Tx.ToString(), signed.ToString());
		Assert.All(signed.Inputs, x => Assert.True(x.HasWitScript()));

		var coins = new[] { alice1Coin, alice2Coin };
		Assert.True(signed.GetVirtualSize() < generousFeeTx.EstimatedVsize);
		Assert.True(feeRate <= signed.GetFeeRate(coins));
		Assert.Equal(generousFeeTx.Balance, signed.GetFee(coins));
	}

	[Fact]
	public void NoDuplicateInputs()
	{
		(var coin, var ownershipProof) = WabiSabiFactory.CreateCoinWithOwnershipProof();
		var state = new ConstructionState(DefaultParameters).AddInput(coin, ownershipProof, CommitmentData);
		ThrowsProtocolException(WabiSabiProtocolErrorCode.NonUniqueInputs, () => state.AddInput(coin, ownershipProof, CommitmentData));
		Assert.Single(state.Inputs);
	}

	// TODO nonstandard input

	[Fact]
	public void OnlyAllowedInputTypes()
	{
		var legacyOnly = new ConstructionState(DefaultParameters with { AllowedInputTypes = ImmutableSortedSet.Create(ScriptType.P2PKH) });
		(var coin, var ownershipProof) = WabiSabiFactory.CreateCoinWithOwnershipProof();
		ThrowsProtocolException(WabiSabiProtocolErrorCode.ScriptNotAllowed, () => legacyOnly.AddInput(coin, ownershipProof, CommitmentData));
	}

	[Fact]
	public void InputAmountRanges()
	{
		(var coin, var ownershipProof) = WabiSabiFactory.CreateCoinWithOwnershipProof();

		var exact = new ConstructionState(DefaultParameters with { AllowedInputAmounts = new MoneyRange(coin.Amount, coin.Amount) });
		var above = new ConstructionState(DefaultParameters with { AllowedInputAmounts = new MoneyRange(2 * coin.Amount, 3 * coin.Amount) });
		var below = new ConstructionState(DefaultParameters with { AllowedInputAmounts = new MoneyRange(coin.Amount - Money.Coins(0.001m), coin.Amount - Money.Coins(0.0001m)) });

		ThrowsProtocolException(WabiSabiProtocolErrorCode.NotEnoughFunds, () => above.AddInput(coin, ownershipProof, CommitmentData));
		ThrowsProtocolException(WabiSabiProtocolErrorCode.TooMuchFunds, () => below.AddInput(coin, ownershipProof, CommitmentData));

		// Allowed range is inclusive:
		Assert.Equal(coin.Amount, Assert.Single(exact.AddInput(coin, ownershipProof, CommitmentData).Inputs).Amount);
	}

	[Fact]
	public void UneconomicalInputs()
	{
		(var alice1Coin, var alice1OwnershipProof) = WabiSabiFactory.CreateCoinWithOwnershipProof(amount: new Money(1000L));
		(var alice2Coin, var alice2OwnershipProof) = WabiSabiFactory.CreateCoinWithOwnershipProof(amount: new Money(2000L));

		// requires 1k sats per input in sat/vKB
		var inputVsize = alice1Coin.ScriptPubKey.EstimateInputVsize();
		var feeRate = new FeeRate(new Money((1_000_000L + inputVsize - 1) / inputVsize));
		Assert.Equal(new Money(1000L), feeRate.GetFee(alice1Coin.ScriptPubKey.EstimateInputVsize()));

		var state = new ConstructionState(DefaultParameters with { MiningFeeRate = feeRate });

		ThrowsProtocolException(WabiSabiProtocolErrorCode.UneconomicalInput, () => state.AddInput(alice1Coin, alice1OwnershipProof, CommitmentData));

		Assert.Equal(alice2Coin.Amount, Assert.Single(state.AddInput(alice2Coin, alice2OwnershipProof, CommitmentData).Inputs).Amount);
	}

	[Fact]
	public void NoNonStandardOutput()
	{
		var state = new ConstructionState(DefaultParameters);
		var sha256Bounty = new TxOut(Money.Coins(1), Script.FromHex("aa20000000000019d6689c085ae165831e934ff763ae46a2a6c172b3f1b60a8ce26f87"));
		ThrowsProtocolException(WabiSabiProtocolErrorCode.NonStandardOutput, () => state.AddOutput(sha256Bounty));
	}

	[Fact]
	public void OnlyAllowedOutputTypes()
	{
		var legacyOnly = new ConstructionState(DefaultParameters with { AllowedOutputTypes = ImmutableSortedSet<ScriptType>.Empty.Add(ScriptType.P2PKH) });
		var p2wpkh = BitcoinFactory.CreateScript();
		ThrowsProtocolException(WabiSabiProtocolErrorCode.ScriptNotAllowed, () => legacyOnly.AddOutput(new TxOut(Money.Coins(1), p2wpkh)));
	}

	[Fact]
	public void OutputAmountRanges()
	{
		var costly = new ConstructionState(DefaultParameters with { AllowedOutputAmounts = new MoneyRange(Money.Coins(7), Money.Coins(11)) });

		var p2wpkh = BitcoinFactory.CreateScript();
		ThrowsProtocolException(WabiSabiProtocolErrorCode.NotEnoughFunds, () => costly.AddOutput(new TxOut(Money.Coins(1), p2wpkh)));
		ThrowsProtocolException(WabiSabiProtocolErrorCode.TooMuchFunds, () => costly.AddOutput(new TxOut(Money.Coins(12), p2wpkh)));

		// Allowed range is inclusive:
		Assert.Equal(Money.Coins(7), Assert.Single(costly.AddOutput(new TxOut(Money.Coins(7), p2wpkh)).Outputs).Value);
		Assert.Equal(Money.Coins(11), Assert.Single(costly.AddOutput(new TxOut(Money.Coins(11), p2wpkh)).Outputs).Value);
	}

	[Fact]
	public void NoDustOutputs()
	{
		var state = new ConstructionState(DefaultParameters);

		var p2wpkh = BitcoinFactory.CreateScript();
		ThrowsProtocolException(WabiSabiProtocolErrorCode.DustOutput, () => state.AddOutput(new TxOut(new Money(1L), p2wpkh)));
		ThrowsProtocolException(WabiSabiProtocolErrorCode.DustOutput, () => state.AddOutput(new TxOut(new Money(293L), p2wpkh)));

		var output = new TxOut(new Money(294L), p2wpkh);
		var updated = state.AddOutput(output);
		Assert.Equal(output, Assert.Single(updated.Outputs));
	}

	[Theory]
	[InlineData(100, 100, "0.2")]
	[InlineData(200, 230, "0.8")]
	[InlineData(150, 170, "1.675")]
	[InlineData(100, 120, "2.7")]
	[InlineData(100, 120, "4.9")]
	[InlineData(300, 330, "10.6")]
	[InlineData(100, 120, "20.7")]
	[InlineData(100, 140, "100")]
	[InlineData(100, 105, "0")]
	public void FeeTests(int inputCount, int outputCount, string feeRateString)
	{
		Random random = new(12345);

		FeeRate feeRate = new(satoshiPerByte: decimal.Parse(feeRateString));
		CoordinationFeeRate coordinatorFeeRate = CoordinationFeeRate.Zero;

		var parameters = WabiSabiFactory.CreateRoundParameters(new()
		{
			MinRegistrableAmount = Money.Zero,
			MaxRegistrableAmount = Money.Coins(43000m),
			MaxSuggestedAmountBase = Money.Coins(Constants.MaximumNumberOfBitcoins)
		}) with
		{
			MiningFeeRate = feeRate
		};

		var coinjoin = new ConstructionState(parameters);

		for (int i = 0; i < inputCount; i++)
		{
			using Key key = new();
			(var aliceCoin, var aliceOwnershipProof) = WabiSabiFactory.CreateCoinWithOwnershipProof(key);
			coinjoin = coinjoin.AddInput(aliceCoin, aliceOwnershipProof, CommitmentData);
		}

		var totalInputEffSum = Money.Satoshis(coinjoin.Inputs.Sum(c => c.EffectiveValue(feeRate, coordinatorFeeRate)));

		// Threshold for stop adding outputs. This will emulate missing outputs in the CJ so blame script will be added.
		var tenPercent = Money.Satoshis((long)(totalInputEffSum.Satoshi * 0.1));

		var outputCoinNominal = Money.Satoshis(totalInputEffSum.Satoshi / outputCount);

		do
		{
			var min = (int)(outputCoinNominal.Satoshi * 0.7);
			var max = (int)(outputCoinNominal.Satoshi * 1.3);
			var amount = Money.Satoshis(random.Next(min, max));
			var p2wpkh = BitcoinFactory.CreateScript();
			coinjoin = coinjoin.AddOutput(new TxOut(amount, p2wpkh));
		}
		while (coinjoin.Balance > tenPercent);

		var coordinatorScript = BitcoinFactory.CreateScript();
		var round = WabiSabiFactory.CreateRound(parameters);

		// Make sure the highest fee rate is low, so coordinator script will be added.
		var coinjoinWithCoordinatorScript = Arena.AddCoordinationFee(round, coinjoin, coordinatorScript);
		coinjoinWithCoordinatorScript.Finalize();
		Assert.NotSame(coinjoinWithCoordinatorScript, coinjoin);
	}
}

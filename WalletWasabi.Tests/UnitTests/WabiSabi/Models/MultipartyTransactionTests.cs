using NBitcoin;
using System;
using System.Collections.Immutable;
using System.Linq;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;
using WalletWasabi.Tests.Helpers;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Models
{
	public class MultipartyTransactionTests
	{
		private static MoneyRange DefaultAllowedAmounts = new(Money.Zero, Money.Coins(1));
		private static MultipartyTransactionParameters DefaultParameters = new(FeeRate.Zero, DefaultAllowedAmounts, DefaultAllowedAmounts, Network.Main);

		private static void ThrowsProtocolException(WabiSabiProtocolErrorCode expectedError, Action action) =>
			Assert.Equal(expectedError, Assert.Throws<WabiSabiProtocolException>(action).ErrorCode);

		[Fact]
		public void TwoPartiesNoFees()
		{
			using Key key1 = new();
			using Key key2 = new();

			var alice1Coin = CreateCoin(script: key1.PubKey.WitHash.ScriptPubKey);
			var alice2Coin = CreateCoin(script: key2.PubKey.WitHash.ScriptPubKey);

			var state = new ConstructionState(DefaultParameters);

			Assert.Empty(state.Inputs);
			Assert.Empty(state.Outputs);

			var oneInput = state.AddInput(alice1Coin);

			Assert.Single(oneInput.Inputs);
			Assert.Empty(oneInput.Outputs);

			// Previous state should be unmodified
			Assert.Empty(state.Inputs);
			Assert.Empty(state.Outputs);

			var differentInput = state.AddInput(alice2Coin);

			Assert.Single(differentInput.Inputs);
			Assert.Empty(differentInput.Outputs);
			Assert.NotEqual(oneInput.Inputs, differentInput.Inputs);
			Assert.Equal(oneInput.Outputs, differentInput.Outputs);

			var twoInputs = oneInput.AddInput(alice2Coin);

			Assert.Equal(2, twoInputs.Inputs.Count);
			Assert.Empty(twoInputs.Outputs);

			// address reuse bad
			var bob1 = new TxOut(Money.Coins(1), alice1Coin.ScriptPubKey);
			var withOutput = twoInputs.AddOutput(bob1);

			Assert.Equal(2, withOutput.Inputs.Count);
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

			var alice1Sig = noFeeTx.AddWitness(0, alice1Tx.Inputs[0].WitScript);
			Assert.True(alice1Sig.IsInputSigned(0));
			Assert.False(alice1Sig.IsInputSigned(1));
			Assert.False(alice1Sig.IsFullySigned);
			Assert.Equal(alice1Tx.ToString(), alice1Sig.CreateTransaction().ToString());

			var alice2Tx = tx.Clone();
			alice2Tx.Sign(key2.GetBitcoinSecret(Network.Main), alice2Coin);

			var alice2Sig = alice1Sig.AddWitness(1, alice2Tx.Inputs[1].WitScript);
			Assert.True(alice2Sig.IsInputSigned(0));
			Assert.True(alice2Sig.IsInputSigned(1));
			Assert.True(alice2Sig.IsFullySigned);

			var signed = alice2Sig.CreateTransaction();
			Assert.NotEqual(alice1Tx.ToString(), signed.ToString());
			Assert.NotEqual(alice2Tx.ToString(), signed.ToString());
			Assert.True(signed.Inputs.All(x => x.HasWitScript()));
		}

		[Fact]
		public void AddWithOptimize()
		{
			var coin = CreateCoin();

			var state = new ConstructionState(DefaultParameters).AddInput(coin);

			var script = BitcoinFactory.CreateScript();
			var bob = new TxOut(coin.Amount/2, script);
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

			var alice1Coin = CreateCoin(script: key1.PubKey.WitHash.ScriptPubKey);
			var alice2Coin = CreateCoin(script: key1.PubKey.WitHash.ScriptPubKey);

			var state = new ConstructionState(DefaultParameters).AddInput(alice1Coin).AddInput(alice2Coin);

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

			// Only accept valid witnesses
			ThrowsProtocolException(WabiSabiProtocolErrorCode.WrongCoinjoinSignature, () => noFeeTx.AddWitness(1, alice1Tx.Inputs[0].WitScript));
			ThrowsProtocolException(WabiSabiProtocolErrorCode.WrongCoinjoinSignature, () => noFeeTx.AddWitness(1, alice1Tx.Inputs[1].WitScript));
			ThrowsProtocolException(WabiSabiProtocolErrorCode.WrongCoinjoinSignature, () => noFeeTx.AddWitness(0, alice1Tx.Inputs[1].WitScript));
			ThrowsProtocolException(WabiSabiProtocolErrorCode.WrongCoinjoinSignature, () => noFeeTx.AddWitness(0, alice2Tx.Inputs[0].WitScript));
			ThrowsProtocolException(WabiSabiProtocolErrorCode.WrongCoinjoinSignature, () => noFeeTx.AddWitness(0, alice2Tx.Inputs[1].WitScript));
			ThrowsProtocolException(WabiSabiProtocolErrorCode.WrongCoinjoinSignature, () => noFeeTx.AddWitness(1, alice2Tx.Inputs[0].WitScript));

			// Add Alice 1's signature
			var alice1Sig = noFeeTx.AddWitness(0, alice1Tx.Inputs[0].WitScript);
			Assert.False(alice1Sig.IsFullySigned);

			// Witness can only be accepted once per input
			ThrowsProtocolException(WabiSabiProtocolErrorCode.WitnessAlreadyProvided, () => alice1Sig.AddWitness(0, alice1Tx.Inputs[0].WitScript));
		}

		[Fact]
		public void FeeRateValidation()
		{
			var feeRate = new FeeRate(new Money(1000L));

			using Key key1 = new();
			using Key key2 = new();

			var alice1Coin = CreateCoin(script: key1.PubKey.WitHash.ScriptPubKey);
			var alice2Coin = CreateCoin(script: key2.PubKey.WitHash.ScriptPubKey);

			var state = new ConstructionState(DefaultParameters with { FeeRate = feeRate })
				.AddInput(alice1Coin)
				.AddInput(alice2Coin);

			var bob1 = new TxOut(Money.Coins(1), alice1Coin.ScriptPubKey);
			var withOutput = state.AddOutput(bob1);

			Assert.Equal(2, withOutput.Inputs.Count);
			Assert.Single(withOutput.Outputs);

			var bob2 = new TxOut(Money.Coins(1), alice2Coin.ScriptPubKey);
			ThrowsProtocolException(WabiSabiProtocolErrorCode.InsufficientFees, () => withOutput.AddOutput(bob2).Finalize());

			bob2 = new TxOut(Money.Coins(0.9999m), alice2Coin.ScriptPubKey);
			var generousFeeTx = withOutput.AddOutput(bob2).Finalize();

			var tx = generousFeeTx.CreateUnsignedTransaction();

			Assert.Collection(
				tx.Inputs.Select(x => x.PrevOut),
				outpoint => Assert.Equal(alice1Coin.Outpoint, outpoint),
				outpoint => Assert.Equal(alice2Coin.Outpoint, outpoint));

			Assert.Collection(
				tx.Outputs.Select(x => x.ScriptPubKey),
				scriptPubKey => Assert.Equal(alice1Coin.ScriptPubKey, scriptPubKey),
				scriptPubKey => Assert.Equal(alice2Coin.ScriptPubKey, scriptPubKey));

			var alice1Tx = tx.Clone();
			alice1Tx.Sign(key1.GetBitcoinSecret(Network.Main), alice1Coin);

			var alice1Sig = generousFeeTx.AddWitness(0, alice1Tx.Inputs[0].WitScript);
			Assert.False(alice1Sig.IsFullySigned);
			Assert.Equal(alice1Tx.ToString(), alice1Sig.CreateTransaction().ToString());

			var alice2Tx = tx.Clone();
			alice2Tx.Sign(key2.GetBitcoinSecret(Network.Main), alice2Coin);

			var alice2Sig = alice1Sig.AddWitness(1, alice2Tx.Inputs[1].WitScript);
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
			var coin = CreateCoin();
			var state = new ConstructionState(DefaultParameters).AddInput(coin);
			ThrowsProtocolException(WabiSabiProtocolErrorCode.NonUniqueInputs, () => state.AddInput(coin));
			Assert.Single(state.Inputs);
		}

		// TODO nonstandard input

		[Fact]
		public void OnlyAllowedInputTypes()
		{
			var legacyOnly = new ConstructionState(DefaultParameters with { AllowedInputTypes = ImmutableSortedSet.Create<ScriptType>(ScriptType.P2PKH) });
			var coin = CreateCoin();
			ThrowsProtocolException(WabiSabiProtocolErrorCode.ScriptNotAllowed, () => legacyOnly.AddInput(coin));
		}

		[Fact]
		public void InputAmountRanges()
		{
			var coin = CreateCoin();

			var exact = new ConstructionState(DefaultParameters with { AllowedInputAmounts = new MoneyRange(coin.Amount, coin.Amount) });
			var above = new ConstructionState(DefaultParameters with { AllowedInputAmounts = new MoneyRange(2 * coin.Amount, 3 * coin.Amount) });
			var below = new ConstructionState(DefaultParameters with { AllowedInputAmounts = new MoneyRange(coin.Amount - Money.Coins(0.001m), coin.Amount - Money.Coins(0.0001m)) });

			ThrowsProtocolException(WabiSabiProtocolErrorCode.NotEnoughFunds, () => above.AddInput(coin));
			ThrowsProtocolException(WabiSabiProtocolErrorCode.TooMuchFunds, () => below.AddInput(coin));

			// Allowed range is inclusive:
			Assert.Equal(coin.Amount, Assert.Single(exact.AddInput(coin).Inputs).Amount);
		}

		[Fact]
		public void UneconomicalInputs()
		{
			var alice1Coin = CreateCoin(new Money(1000L));
			var alice2Coin = CreateCoin(new Money(1001L));

			// requires 1k sats per input in sat/vKB
			var inputVsize = alice1Coin.ScriptPubKey.EstimateInputVsize();
			var feeRate = new FeeRate(new Money((1_000_000L + inputVsize-1) / inputVsize));
			Assert.Equal(new Money(1000L), feeRate.GetFee(alice1Coin.ScriptPubKey.EstimateInputVsize()));

			var state = new ConstructionState(DefaultParameters with { FeeRate = feeRate });

			ThrowsProtocolException(WabiSabiProtocolErrorCode.UneconomicalInput, () => state.AddInput(alice1Coin));

			Assert.Equal(alice2Coin.Amount, Assert.Single(state.AddInput(alice2Coin).Inputs).Amount);
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

		private Coin CreateCoin(Money? amount = null, Script? script = null)
			=> new Coin(
				BitcoinFactory.CreateOutPoint(),
				new TxOut(amount ?? Money.Coins(1), script ?? BitcoinFactory.CreateScript()));
	}
}

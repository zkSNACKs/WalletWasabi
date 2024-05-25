using NBitcoin;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Models;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.Blockchain.TransactionOutputs;

/// <summary>
/// Tests for <see cref="CoinsRegistry"/> class.
/// </summary>
public class CoinsRegistryTests
{
	public CoinsRegistryTests()
	{
		KeyManager = KeyManager.CreateNew(out _, password: "password", Network.Main);
	}

	private KeyManager KeyManager { get; }
	private CoinsRegistry Coins { get; } = new();

	/// <summary>
	/// Tests <see cref="CoinsRegistry.DescendantOf(SmartCoin)"/> method.
	/// </summary>
	[Fact]
	public void DescendantsOf()
	{
		using CancellationTokenSource testDeadlineCts = new(TimeSpan.FromSeconds(30));

		// --tx0---> (A) --tx1---> (B) --tx2---> (D)
		//                          |
		//                          +--> (C)

		SmartTransaction tx0; // The transaction has 2 inputs and 1 output.
		SmartTransaction tx1; // The transaction has 1 input and 2 outputs.
		SmartTransaction tx2; // The transaction has 1 input and 1 output.

		SmartCoin tx0Coin;
		SmartCoin tx1Coin1;
		SmartCoin tx1Coin2;
		SmartCoin tx2Coin;

		IReadOnlyList<SmartCoin> tx0Coins;
		IReadOnlyList<SmartCoin> tx1Coins;
		IReadOnlyList<SmartCoin> tx2Coins;

		// Create and process transaction tx0, tx1, and tx2.
		{
			// Tx0.
			tx0 = CreateCreditingTransaction(NewInternalKey(label: "A").P2wpkhScript, Money.Coins(1.0m), height: 54321);
			tx0Coins = ProcessTransaction(tx0);
			tx0Coin = Assert.Single(tx0Coins);

			// Tx1.
			tx1 = CreateSpendingTransaction(
				tx0Coin.Coin,
				new TxOut(Money.Coins(0.85m), NewInternalKey(label: "B").P2wpkhScript),
				new TxOut(Money.Coins(0.1m), NewInternalKey("C").P2wpkhScript));
			tx1.Transaction.Inputs[0].Sequence = Sequence.OptInRBF;
			tx1Coins = ProcessTransaction(tx1);
			Assert.Equal(2, tx1Coins.Count);
			tx1Coin1 = tx1Coins[0];
			tx1Coin2 = tx1Coins[1];

			// Tx2.
			SmartCoin coin = Assert.Single(Coins, coin => coin.HdPubKey.Labels == "B");
			tx2 = CreateSpendingTransaction(coin.Coin, txOuts: new TxOut(Money.Coins(0.7m), NewInternalKey("D").P2wpkhScript));
			tx2Coins = ProcessTransaction(tx2);
			tx2Coin = Assert.Single(tx2Coins);
		}

		// Descendants of tx0-0 coin.
		{
			IReadOnlySet<SmartCoin> allCoins = tx0Coins.Concat(tx1Coins).Concat(tx2Coins).ToHashSet();
			IReadOnlySet<SmartCoin> actualCoins = Coins.DescendantOf(tx0Coin, includeSelf: true).ToHashSet();
			Assert.Equal(allCoins, actualCoins);
		}

		// Descendants of tx1-0 coin.
		{
			IReadOnlySet<SmartCoin> expectedCoins = tx2Coins.Concat(new HashSet<SmartCoin>() { tx1Coin1 }).ToHashSet();
			IReadOnlySet<SmartCoin> actualCoins = Coins.DescendantOf(tx1Coin1, includeSelf: true).ToHashSet();
			Assert.Equal(expectedCoins, actualCoins);
		}

		// Descendants of tx1-1 coin.
		{
			IReadOnlySet<SmartCoin> expectedCoins = new HashSet<SmartCoin>() { tx1Coin2 };
			IReadOnlySet<SmartCoin> actualCoins = Coins.DescendantOf(tx1Coin2, includeSelf: true).ToHashSet();
			Assert.Equal(expectedCoins, actualCoins);
		}

		// Descendants of tx2-0 coin.
		{
			IReadOnlySet<SmartCoin> expectedCoins = new HashSet<SmartCoin>() { tx2Coin };
			IReadOnlySet<SmartCoin> actualCoins = Coins.DescendantOf(tx2Coin, includeSelf: true).ToHashSet();
			Assert.Equal(expectedCoins, actualCoins);
		}
	}

	/// <summary>
	/// Tests <see cref="CoinsRegistry.Undo(uint256)"/> method with respect to caching of prevOuts properly.
	/// </summary>
	[Fact]
	public void UndoTransaction_PrevOutCache()
	{
		using CancellationTokenSource testDeadlineCts = new(TimeSpan.FromSeconds(30));

		// --tx0---> (A) --tx1 (replaceable)-+--> (B) --tx2---> (D)
		//                  |                |
		//                  |                +--> (C)
		//                  |
		//                  +--tx3 (replacement)---> (E)

		Coin? tx0Coin;
		SmartTransaction tx0; // The transaction has 2 inputs and 1 output.
		SmartTransaction tx1; // The transaction has 1 input and 2 outputs.
		SmartTransaction tx2; // The transaction has 1 input and 1 output.
		SmartTransaction tx3; // The transaction has 1 input and 1 output.

		// Create and process transaction tx0.
		{
			tx0 = CreateCreditingTransaction(NewInternalKey(label: "A").P2wpkhScript, Money.Coins(1.0m), height: 54321);
			tx0Coin = tx0.Transaction.Outputs.AsCoins().First();

			IReadOnlyList<SmartCoin> tx0Coins = ProcessTransaction(tx0);
			Assert.Single(tx0Coins);
			Assert.Single(Coins);
			Assert.Equal(tx0Coins[0], Coins.First());

			// Verify that both tx0 inputs' prevOuts are set to be spent by the single tx0's coin.
			foreach (var input in tx0.Transaction.Inputs)
			{
				Assert.True(Coins.TryGetCoinsByInputPrevOut(input.PrevOut, out var coinsSpendingTx0PrevOuts));
				Assert.Single(coinsSpendingTx0PrevOuts);
			}
		}

		// Create and process transaction tx1 that fully spends tx0.
		{
			tx1 = CreateSpendingTransaction(
				tx0Coin,
				new TxOut(Money.Coins(0.85m), NewInternalKey(label: "B").P2wpkhScript),
				new TxOut(Money.Coins(0.1m), NewInternalKey("C").P2wpkhScript));
			tx1.Transaction.Inputs[0].Sequence = Sequence.OptInRBF;

			IReadOnlyList<SmartCoin> tx1Coins = ProcessTransaction(tx1);
			AssertEqualCoinSets(Coins, tx1Coins);

			SmartCoin unconfirmedCoin1 = Assert.Single(Coins, coin => coin.HdPubKey.Labels == "B");
			SmartCoin unconfirmedCoin2 = Assert.Single(Coins, coin => coin.HdPubKey.Labels == "C");
			Assert.True(unconfirmedCoin1.Transaction.IsRBF);
			Assert.True(unconfirmedCoin2.Transaction.IsRBF);

			Assert.True(Coins.IsKnown(tx0.GetHash()));
			Assert.True(Coins.IsKnown(tx1.GetHash()));

			// Verify that we cache properly the coins spending inputs' prevOuts of tx0 and tx1.
			{
				// Check both tx0 inputs.
				foreach (var input in tx0.Transaction.Inputs)
				{
					Assert.True(Coins.TryGetCoinsByInputPrevOut(input.PrevOut, out var coinsSpendingTx0PrevOuts));
					Assert.Single(coinsSpendingTx0PrevOuts);
				}

				// Check the input of tx1. Both tx1's outputs should be found.
				TxIn tx1Input = Assert.Single(tx1.Transaction.Inputs);
				Assert.True(Coins.TryGetCoinsByInputPrevOut(tx1Input.PrevOut, out var coinsSpendingTx1PrevOut));
				Assert.Equal(2, coinsSpendingTx1PrevOut.Count);
			}
		}

		// Create and process transaction tx2 that partially spends tx1.
		{
			SmartCoin coin = Assert.Single(Coins, coin => coin.HdPubKey.Labels == "B");

			tx2 = CreateSpendingTransaction(coin.Coin, txOuts: new TxOut(Money.Coins(0.7m), NewInternalKey("D").P2wpkhScript));

			IReadOnlyList<SmartCoin> tx2Coins = ProcessTransaction(tx2);
			Assert.Single(tx2Coins);

			Assert.True(Coins.IsKnown(tx1.GetHash()));
			Assert.True(Coins.IsKnown(tx2.GetHash()));

			// Verify that we cache properly the coins spending inputs' prevOuts of tx0, tx1 and tx2.
			{
				// No change for tx0 inputs is expected.
				foreach (TxIn input in tx0.Transaction.Inputs)
				{
					Assert.True(Coins.TryGetCoinsByInputPrevOut(input.PrevOut, out var coinsSpendingTx0PrevOuts));
					Assert.Single(coinsSpendingTx0PrevOuts);
				}

				// No change for the tx1's input is expected.
				TxIn tx1Input = Assert.Single(tx1.Transaction.Inputs);
				Assert.True(Coins.TryGetCoinsByInputPrevOut(tx1Input.PrevOut, out var coinsSpendingTx1PrevOut));
				Assert.Equal(2, coinsSpendingTx1PrevOut.Count);

				// Input of tx2 must be processed correctly.
				TxIn tx2Input = Assert.Single(tx2.Transaction.Inputs);
				Assert.True(Coins.TryGetCoinsByInputPrevOut(tx2Input.PrevOut, out var coinsSpendingTx2PrevOut));
				Assert.Single(coinsSpendingTx2PrevOut);
			}
		}

		// Create and process REPLACEMENT transaction tx3 that fully spends tx0.
		{
			// Undo tx1.
			{
				Assert.True(Coins.IsKnown(tx0.GetHash()));
				Assert.True(Coins.IsKnown(tx1.GetHash()));
				Assert.True(Coins.IsKnown(tx2.GetHash()));

				// First undo tx1 which should transitively undo tx2.
				(ICoinsView toRemove, ICoinsView toAdd) = Coins.Undo(tx1.GetHash());

				Assert.True(Coins.IsKnown(tx0.GetHash()));
				Assert.False(Coins.IsKnown(tx1.GetHash()));
				Assert.False(Coins.IsKnown(tx2.GetHash()));

				foreach (var input in tx0.Transaction.Inputs)
				{
					Assert.True(Coins.TryGetCoinsByInputPrevOut(input.PrevOut, out var coinsSpendingPrevOut));
					Assert.Single(coinsSpendingPrevOut);
				}

				TxIn tx1Input = Assert.Single(tx1.Transaction.Inputs);
				Assert.False(Coins.TryGetCoinsByInputPrevOut(tx1Input.PrevOut, out var coinsSpendingTx1PrevOut));
				Assert.Null(coinsSpendingTx1PrevOut);

				TxIn tx2Input = Assert.Single(tx2.Transaction.Inputs);
				Assert.False(Coins.TryGetCoinsByInputPrevOut(tx2Input.PrevOut, out var coinsSpendingTx2PrevOut));
				Assert.Null(coinsSpendingTx2PrevOut);
			}

			// .. then create and process tx3.
			{
				tx3 = CreateSpendingTransaction(tx0Coin, txOuts: new TxOut(Money.Coins(0.9m), NewInternalKey("E").P2wpkhScript));
				IReadOnlyList<SmartCoin> tx3Coins = ProcessTransaction(tx3);

				SmartCoin finalCoin = Assert.Single(Coins);
				Assert.Equal("E", finalCoin.HdPubKey.Labels);

				Assert.Empty(Coins.AsAllCoinsView().Where(coin => coin.HdPubKey.Labels == "B"));
				Assert.Empty(Coins.AsAllCoinsView().Where(coin => coin.HdPubKey.Labels == "C"));
				Assert.Empty(Coins.AsAllCoinsView().Where(coin => coin.HdPubKey.Labels == "D"));

				// Replaced transactions tx1 and tx2 have to be removed because tx3 replaced tx1.
				Assert.False(Coins.IsKnown(tx1.GetHash()));
				Assert.False(Coins.IsKnown(tx2.GetHash()));
				Assert.True(Coins.IsKnown(tx3.GetHash()));

				TxIn tx3Input = Assert.Single(tx3.Transaction.Inputs);
				Assert.True(Coins.TryGetCoinsByInputPrevOut(tx3Input.PrevOut, out var coinsSpendingTx3PrevOut));
				Assert.Single(coinsSpendingTx3PrevOut);
			}
		}
	}

	/// <summary>
	/// Tests <see cref="CoinsRegistry.Undo(uint256)"/> method with respect to reporting proper transaction amounts.
	/// </summary>
	[Fact]
	public void UndoTransaction_TransactionAmounts()
	{
		using CancellationTokenSource testDeadlineCts = new(TimeSpan.FromSeconds(30));

		// --tx0---> (A) --tx1 (replaceable)-+--> (B) --tx2---> (D)
		//                  |                |
		//                  |                +--> (C)
		//                  |
		//                  +--tx3 (replacement)---> (E)

		Coin? tx0Coin;
		SmartTransaction tx0; // The transaction has 2 inputs and 1 output.
		SmartTransaction tx1; // The transaction has 1 input and 2 outputs.
		SmartTransaction tx2; // The transaction has 1 input and 1 output.
		SmartTransaction tx3; // The transaction has 1 input and 1 output.

		Money tx0CreditingAmount = Money.Coins(1.0m);
		Money expectedTx1Amount = Money.Coins(-0.05m); // BTC 0.05 was spent (on fees).
		Money expectedTx2Amount = Money.Coins(-0.15m); // BTC 0.15 was spent (on fees).
		Money expectedTx3Amount = Money.Coins(-0.10m); // BTC 0.10 was spent (on fees).

		// Create and process transaction tx0.
		{
			tx0 = CreateCreditingTransaction(NewInternalKey(label: "A").P2wpkhScript, tx0CreditingAmount, height: 54321);
			tx0Coin = tx0.Transaction.Outputs.AsCoins().First();

			Assert.Equal(Money.Zero, Coins.GetTotalBalance());

			// Now process tx0.
			IReadOnlyList<SmartCoin> tx0Coins = ProcessTransaction(tx0);
			Assert.Single(tx0Coins);
			Assert.Single(Coins);
			Assert.Equal(tx0Coins[0], Coins.First());

			// Tx0's inputs are made up. So money appears out of thin air, but the result is correct.
			AssertTransactionAmount(tx0, expectedAmount: tx0CreditingAmount);
			Assert.Equal(tx0CreditingAmount, Coins.GetTotalBalance());
		}

		// Create and process transaction tx1 that fully spends tx0.
		{
			tx1 = CreateSpendingTransaction(
				tx0Coin,
				new TxOut(Money.Coins(0.85m), NewInternalKey(label: "B").P2wpkhScript),
				new TxOut(Money.Coins(0.1m), NewInternalKey("C").P2wpkhScript));
			tx1.Transaction.Inputs[0].Sequence = Sequence.OptInRBF;

			IReadOnlyList<SmartCoin> tx1Coins = ProcessTransaction(tx1);
			AssertEqualCoinSets(Coins, tx1Coins);

			AssertTransactionAmount(tx0, expectedAmount: tx0CreditingAmount);
			AssertTransactionAmount(tx1, expectedAmount: expectedTx1Amount); // +0.85 [tx1-0] + 0.1 [tx1-1] - 1 [tx0] = -0.05.
			Assert.Equal(Money.Coins(0.95m), Coins.GetTotalBalance()); // 1.0 [initially] - 0.05 [tx1] = 0.95 [after-tx1]
		}

		// Create and process transaction tx2 that partially spends tx1.
		{
			SmartCoin coin = Assert.Single(Coins, coin => coin.HdPubKey.Labels == "B");

			tx2 = CreateSpendingTransaction(coin.Coin, txOuts: new TxOut(Money.Coins(0.7m), NewInternalKey("D").P2wpkhScript));

			IReadOnlyList<SmartCoin> tx2Coins = ProcessTransaction(tx2);
			Assert.Single(tx2Coins);

			AssertTransactionAmount(tx0, expectedAmount: tx0CreditingAmount);
			AssertTransactionAmount(tx1, expectedAmount: expectedTx1Amount);
			AssertTransactionAmount(tx2, expectedAmount: expectedTx2Amount); // +0.7 [tx2-0] - 0.85 [tx1] = -0.15.
			Assert.Equal(Money.Coins(0.8m), Coins.GetTotalBalance()); // 0.95 [after-tx1] - 0.15 [tx2] = 0.8.
		}

		// Create and process REPLACEMENT transaction tx3 that fully spends tx0.
		{
			// Undo tx1.
			{
				// First undo tx1 which should transitively undo tx2.
				(ICoinsView toRemove, ICoinsView toAdd) = Coins.Undo(tx1.GetHash());

				AssertTransactionAmount(tx0, expectedAmount: tx0CreditingAmount);
				AssertNoTransactionAmount(tx1);
				AssertNoTransactionAmount(tx2);
				Assert.Equal(tx0CreditingAmount, Coins.GetTotalBalance()); // 1.0 [initially].
			}

			// .. then create and process tx3.
			{
				tx3 = CreateSpendingTransaction(tx0Coin, txOuts: new TxOut(Money.Coins(0.9m), NewInternalKey("E").P2wpkhScript));
				IReadOnlyList<SmartCoin> tx3Coins = ProcessTransaction(tx3);

				SmartCoin finalCoin = Assert.Single(Coins);
				Assert.Equal("E", finalCoin.HdPubKey.Labels);

				AssertTransactionAmount(tx0, expectedAmount: tx0CreditingAmount);
				AssertTransactionAmount(tx3, expectedAmount: expectedTx3Amount); // +0.9 [tx3-0] - 1.0 [tx0] = -0.1.
				Assert.Equal(Money.Coins(0.9m), Coins.GetTotalBalance()); // 1.0 [initially] - 0.1 [tx3] = 0.9 [after-tx3]
			}
		}
	}

	/// <summary>
	/// Tests that processing twice the same transaction results in a correct and consistent state.
	/// </summary>
	[Fact]
	public void ProcessTwiceSameTransactionTest()
	{
		Money tx0CreditingAmount = Money.Coins(1.0m);

		// Create and process transaction tx0.
		SmartTransaction tx0 = CreateCreditingTransaction(NewInternalKey(label: "A").P2wpkhScript, tx0CreditingAmount, height: 54321);
		Assert.Equal(Money.Zero, Coins.GetTotalBalance());
		Assert.False(Coins.TryGetTxAmount(tx0.GetHash(), out _));

		// Now process tx0 twice.
		ProcessTransaction(tx0);
		Assert.Empty(ProcessTransaction(tx0));

		// There is only a single coin.
		Assert.Single(Coins);

		// Total balance and amount registered for tx0 should be correct.
		Assert.Equal(tx0CreditingAmount, Coins.GetTotalBalance());
		Assert.True(Coins.TryGetTxAmount(tx0.GetHash(), out var tx0Amount));
		Assert.Equal(tx0CreditingAmount, tx0Amount);
	}

	/// <summary>Modify UTXO set in <see cref="CoinsRegistry"/> with <paramref name="tx">transaction</paramref> in mind.</summary>
	private IReadOnlyList<SmartCoin> ProcessTransaction(SmartTransaction tx)
	{
		List<SmartCoin> result = new(capacity: tx.Transaction.Outputs.Count);

		// Add new coins to the registry.
		foreach (Coin coin in tx.Transaction.Outputs.AsCoins())
		{
			if (KeyManager.TryGetKeyForScriptPubKey(coin.ScriptPubKey, out HdPubKey? pubKey))
			{
				SmartCoin newCoin = new(tx, outputIndex: coin.Outpoint.N, pubKey: pubKey);
				if (Coins.TryAdd(newCoin))
				{
					result.Add(newCoin);
				}
			}
		}

		// Remove spent coins from the registry.
		IReadOnlyList<SmartCoin> myInputs = Coins.GetMyInputs(tx);

		foreach (SmartCoin spentCoin in myInputs)
		{
			Coins.Spend(spentCoin, tx);
		}

		return result;
	}

	private static SmartTransaction CreateCreditingTransaction(Script scriptPubKey, Money amount, int height)
	{
		Transaction tx = Network.Main.CreateTransaction();
		tx.Version = 1;
		tx.LockTime = LockTime.Zero;
		tx.Inputs.Add(GetRandomOutPoint(), new Script(OpcodeType.OP_0, OpcodeType.OP_0), sequence: Sequence.Final);
		tx.Inputs.Add(GetRandomOutPoint(), new Script(OpcodeType.OP_0, OpcodeType.OP_0), sequence: Sequence.Final);
		tx.Outputs.Add(amount, scriptPubKey);

		return new SmartTransaction(tx, height == 0 ? Height.Mempool : new Height(height));
	}

	/// <summary>Creates a transaction that fully spends the given coin to a single outpoint (leaving rest for fees).</summary>
	private static SmartTransaction CreateSpendingTransaction(Coin coin, params TxOut[] txOuts)
	{
		var tx = Network.Main.CreateTransaction();
		tx.Inputs.Add(coin.Outpoint, Script.Empty, WitScript.Empty);

		foreach (TxOut txOut in txOuts)
		{
			tx.Outputs.Add(txOut);
		}

		return new SmartTransaction(tx, Height.Mempool);
	}

	private static OutPoint GetRandomOutPoint()
		=> new(RandomUtils.GetUInt256(), 0);

	private HdPubKey NewInternalKey(string label)
		=> KeyManager.GenerateNewKey(label, KeyState.Clean, isInternal: true);

	/// <summary>Compare coins registry and actual coins as two sets (disregarding ordering).</summary>
	private void AssertEqualCoinSets(CoinsRegistry coins, IEnumerable<SmartCoin> actualCoins)
		=> Assert.Equal(new HashSet<SmartCoin>(coins), new HashSet<SmartCoin>(actualCoins));

	/// <summary>Asserts that the transaction is assigned specified amount (representing a balance change for a wallet) in the coins registry.</summary>
	private void AssertTransactionAmount(SmartTransaction tx, Money expectedAmount)
	{
		Assert.True(Coins.TryGetTxAmount(tx.GetHash(), out Money? actualAmount));
		Assert.Equal(expectedAmount, actualAmount);
	}

	private void AssertNoTransactionAmount(SmartTransaction tx)
		=> Assert.False(Coins.TryGetTxAmount(tx.GetHash(), out Money? _));
}

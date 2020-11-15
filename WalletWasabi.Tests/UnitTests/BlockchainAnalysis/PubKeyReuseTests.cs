using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Tests.Helpers;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.BlockchainAnalysis
{
	public class PubKeyReuseTests
	{
		[Fact]
		public void AddressReusePunishment()
		{
			// If there's reuse in input and output side, then output side didn't gain, nor lose anonymity.
			var analyser = BitcoinMock.RandomBlockchainAnalyzer();
			var km = BitcoinMock.RandomKeyManager();
			var reuse = BitcoinMock.RandomHdPubKey(km);
			var tx = BitcoinMock.RandomSmartTransaction(
				9,
				Enumerable.Repeat(Money.Coins(1m), 9),
				new[] { (Money.Coins(1.1m), 100, BitcoinMock.RandomHdPubKey(km)) },
				new[] { (Money.Coins(1m), HdPubKey.DefaultHighAnonymitySet, reuse) });

			// Make the reused key anonymity set something smaller than 109 (which should be the final anonymity set)
			reuse.AnonymitySet = 30;

			analyser.Analyze(tx);

			Assert.All(tx.WalletInputs, x => Assert.Equal(100, x.HdPubKey.AnonymitySet));

			// It should be smaller than 30, because reuse also gets punishment.
			Assert.True(tx.WalletOutputs.First().HdPubKey.AnonymitySet < 30);
		}

		[Fact]
		public void AddressReuseIrrelevantInNormalSpend()
		{
			// In normal transactions we expose to someone that we own the inputs and the changes
			// So we cannot test address reuse here, because anonsets would be 1 regardless of anything.
			var analyser = BitcoinMock.RandomBlockchainAnalyzer();
			var km = BitcoinMock.RandomKeyManager();
			var key = BitcoinMock.RandomHdPubKey(km);
			var tx = BitcoinMock.RandomSmartTransaction(
				0,
				Enumerable.Repeat(Money.Coins(1m), 9),
				new[] { (Money.Coins(1.1m), 100, key), (Money.Coins(1.2m), 100, key), (Money.Coins(1.3m), 100, key), (Money.Coins(1.4m), 100, key) },
				new[] { (Money.Coins(1m), HdPubKey.DefaultHighAnonymitySet, BitcoinMock.RandomHdPubKey(km)) });

			analyser.Analyze(tx);

			Assert.All(tx.WalletInputs, x => Assert.Equal(1, x.HdPubKey.AnonymitySet));
			Assert.Equal(1, tx.WalletOutputs.First().HdPubKey.AnonymitySet);
		}

		[Fact]
		public void InputSideAddressReuseHaveNoConsolidationPunishmentInSelfSpend()
		{
			var analyser = BitcoinMock.RandomBlockchainAnalyzer();
			var km = BitcoinMock.RandomKeyManager();
			var key = BitcoinMock.RandomHdPubKey(km);
			var tx = BitcoinMock.RandomSmartTransaction(
				0,
				Enumerable.Empty<Money>(),
				new[] { (Money.Coins(1.1m), 100, key), (Money.Coins(1.2m), 100, key), (Money.Coins(1.3m), 100, key), (Money.Coins(1.4m), 100, key) },
				new[] { (Money.Coins(1m), HdPubKey.DefaultHighAnonymitySet, BitcoinMock.RandomHdPubKey(km)) }); ;

			analyser.Analyze(tx);

			Assert.All(tx.WalletInputs, x => Assert.Equal(100, x.HdPubKey.AnonymitySet));
			Assert.Equal(100, tx.WalletOutputs.First().HdPubKey.AnonymitySet);
		}

		[Fact]
		public void InputSideAddressReuseHaveNoConsolidationPunishmentInCoinJoin()
		{
			var analyser = BitcoinMock.RandomBlockchainAnalyzer();
			var km = BitcoinMock.RandomKeyManager();
			var key = BitcoinMock.RandomHdPubKey(km);
			var tx = BitcoinMock.RandomSmartTransaction(
				9,
				Enumerable.Repeat(Money.Coins(1m), 9),
				new[] { (Money.Coins(1.1m), 100, key), (Money.Coins(1.2m), 100, key), (Money.Coins(1.3m), 100, key), (Money.Coins(1.4m), 100, key) },
				new[] { (Money.Coins(1m), HdPubKey.DefaultHighAnonymitySet, BitcoinMock.RandomHdPubKey(km)) });

			analyser.Analyze(tx);

			Assert.All(tx.WalletInputs, x => Assert.Equal(100, x.HdPubKey.AnonymitySet));
			Assert.Equal(109, tx.WalletOutputs.First().HdPubKey.AnonymitySet);
		}

		[Fact]
		public void InputOutputSideAddress()
		{
			// If there's reuse in input and output side, then output side didn't gain, nor lose anonymity.
			var analyser = BitcoinMock.RandomBlockchainAnalyzer();
			var key = BitcoinMock.RandomHdPubKey(BitcoinMock.RandomKeyManager());
			var tx = BitcoinMock.RandomSmartTransaction(
				9,
				Enumerable.Repeat(Money.Coins(1m), 9),
				new[] { (Money.Coins(1.1m), 100, key) },
				new[] { (Money.Coins(1m), HdPubKey.DefaultHighAnonymitySet, key) });

			analyser.Analyze(tx);

			Assert.All(tx.WalletInputs, x => Assert.Equal(100, x.HdPubKey.AnonymitySet));
			Assert.Equal(100, tx.WalletOutputs.First().HdPubKey.AnonymitySet);
		}

		[Fact]
		public void OutputSideAddressReusePunished()
		{
			// If there's reuse in input and output side, then output side didn't gain, nor lose anonymity.
			var analyser = BitcoinMock.RandomBlockchainAnalyzer();
			var km = BitcoinMock.RandomKeyManager();
			var key = BitcoinMock.RandomHdPubKey(km);
			var tx = BitcoinMock.RandomSmartTransaction(
				9,
				Enumerable.Repeat(Money.Coins(1m), 9).Concat(Enumerable.Repeat(Money.Coins(2m), 7)),
				new[] { (Money.Coins(1.1m), 100, BitcoinMock.RandomHdPubKey(km)) },
				new[] { (Money.Coins(1m), HdPubKey.DefaultHighAnonymitySet, key), (Money.Coins(2m), HdPubKey.DefaultHighAnonymitySet, key) });

			analyser.Analyze(tx);

			Assert.All(tx.WalletInputs, x => Assert.Equal(100, x.HdPubKey.AnonymitySet));

			// Normally all levels should have 109 and 106 anonsets, but they're consolidated and punished.
			Assert.All(tx.WalletOutputs.Select(x => x.HdPubKey.AnonymitySet), x => Assert.True(x < 106));
		}

		[Fact]
		public void OutputSideAddressReuseDoesntPunishedMoreThanInheritence()
		{
			// If there's reuse in input and output side, then output side didn't gain, nor lose anonymity.
			var analyser = BitcoinMock.RandomBlockchainAnalyzer();
			var km = BitcoinMock.RandomKeyManager();
			var key = BitcoinMock.RandomHdPubKey(km);
			var tx = BitcoinMock.RandomSmartTransaction(
				9,
				Enumerable.Repeat(Money.Coins(1m), 9).Concat(Enumerable.Repeat(Money.Coins(2m), 8)).Concat(Enumerable.Repeat(Money.Coins(3m), 7)).Concat(Enumerable.Repeat(Money.Coins(4m), 6)).Concat(Enumerable.Repeat(Money.Coins(5m), 5)).Concat(Enumerable.Repeat(Money.Coins(6m), 4)),
				new[] { (Money.Coins(1.1m), 100, BitcoinMock.RandomHdPubKey(km)) },
				new[] { (Money.Coins(1m), HdPubKey.DefaultHighAnonymitySet, key), (Money.Coins(2m), HdPubKey.DefaultHighAnonymitySet, key), (Money.Coins(3m), HdPubKey.DefaultHighAnonymitySet, key), (Money.Coins(4m), HdPubKey.DefaultHighAnonymitySet, key), (Money.Coins(5m), HdPubKey.DefaultHighAnonymitySet, key), (Money.Coins(6m), HdPubKey.DefaultHighAnonymitySet, key) });

			analyser.Analyze(tx);

			Assert.All(tx.WalletInputs, x => Assert.Equal(100, x.HdPubKey.AnonymitySet));

			// 100 is the input anonset, so outputs shouldn't go lower than that.
			Assert.All(tx.WalletOutputs.Select(x => x.HdPubKey.AnonymitySet), x => Assert.True(x >= 100));
		}
	}
}

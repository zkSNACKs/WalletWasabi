using NBitcoin;
using System.Collections.Generic;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Models;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WabiSabi.Client.Batching;
using WalletWasabi.Wallets;

namespace WalletWasabi.Tests.UnitTests.CoinJoinManager;

internal class MockWallet : IWallet
{
	public MockWallet(bool isUnderPlebStop, bool isWalletPrivate = false, bool addBatchedPayment = false)
	{
		IsUnderPlebStop = isUnderPlebStop;
		IsWalletPrivate = isWalletPrivate;
		BatchedPayments = new PaymentBatch();
		if (addBatchedPayment)
		{
			BatchedPayments.AddPayment(GetNewSegwitAddress(), Money.Coins(1));
		}
	}

	private bool IsWalletPrivate { get; set; }

	public string WalletName => throw new NotImplementedException();

	public WalletId WalletId => throw new NotImplementedException();

	public bool IsUnderPlebStop { get; set; }

	public bool IsMixable => throw new NotImplementedException();

	public IKeyChain? KeyChain => throw new NotImplementedException();

	public IDestinationProvider DestinationProvider => throw new NotImplementedException();

	public int AnonScoreTarget => throw new NotImplementedException();

	public bool ConsolidationMode => throw new NotImplementedException();

	public TimeSpan FeeRateMedianTimeFrame => throw new NotImplementedException();

	public bool RedCoinIsolation => throw new NotImplementedException();

	public CoinjoinSkipFactors CoinjoinSkipFactors => throw new NotImplementedException();

	public Task<IEnumerable<SmartCoin>> GetCoinjoinCoinCandidatesAsync()
	{
		throw new NotImplementedException();
	}

	public Task<IEnumerable<SmartTransaction>> GetTransactionsAsync()
	{
		throw new NotImplementedException();
	}

	public Task<bool> IsWalletPrivateAsync()
	{
		return Task.FromResult(IsWalletPrivate);
	}

	public PaymentBatch BatchedPayments { get; }

	private static BitcoinAddress GetNewSegwitAddress()
	{
		using Key key = new();
		return key.PubKey.GetAddress(ScriptPubKeyType.Segwit, Network.Main);
	}
}

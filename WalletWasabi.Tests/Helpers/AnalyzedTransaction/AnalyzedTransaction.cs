using NBitcoin;
using System.Linq;
using System.Collections.Generic;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Blockchain.Transactions;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using WalletWasabi.Blockchain.Analysis.Clustering;

namespace WalletWasabi.Tests.Helpers.AnalyzedTransaction;

public class AnalyzedTransaction : SmartTransaction
{
	public AnalyzedTransaction()
		: base(Transaction.Create(Network.Main), 0)
	{
	}

	private static Key CreateKey(string? label = null)
	{
		if (label is null)
			return new Key();
		else
			return new Key(Hashes.DoubleSHA256(Encoders.ASCII.DecodeData(label)).ToBytes());
	}

	private static Script CreateScript(string? label = null)
	{
		return CreateKey(label).PubKey.WitHash.ScriptPubKey;
	}

	private static HdPubKey CreateHdPubKey(string? label = null)
	{
		return new HdPubKey(CreateKey(label).PubKey, new KeyPath("0/0/0/0/0"), SmartLabel.Empty, KeyState.Clean);
	}

	public void AddForeignInput(ForeignOutput output)
	{
		Transaction.Inputs.Add(output.ToOutPoint());
	}

	public ForeignOutput AddForeignInput(decimal amount = 1, string? label = null)
	{
		ForeignOutput foreignOutput = ForeignOutput.Create(new Money(amount, MoneyUnit.BTC), CreateScript(label));
		AddForeignInput(foreignOutput);
		return foreignOutput;
	}

	private static AnalyzedTransaction CreateCoinjoin(int foreignInputs, int foreignOutputs)
	{
		AnalyzedTransaction transaction = new();
		for (int i = 0; i < foreignInputs; i++)
		{
			transaction.AddForeignInput();
		}
		for (int i = 0; i < foreignOutputs; i++)
		{
			transaction.AddForeignOutput();
		}
		return transaction;
	}

	public void AddWalletInput(WalletOutput walletOutput)
	{
		AddForeignInput(walletOutput.ToForeignOutput());
		AddWalletInput(walletOutput.ToSmartCoin());
	}

	public WalletOutput AddWalletInput(decimal amount = 1, string? label = null, int anonymity = 1)
	{
		AnalyzedTransaction coinjoinTransaction = AnalyzedTransaction.CreateCoinjoin(anonymity - 1, anonymity - 1);
		coinjoinTransaction.AddWalletInput(WalletOutput.Create(new Money(amount, MoneyUnit.BTC), CreateHdPubKey()));
		WalletOutput walletOutput = coinjoinTransaction.AddWalletOutput(amount, label);
		AddWalletInput(walletOutput);
		return walletOutput;
	}

	public ForeignOutput AddForeignOutput(decimal amount = 1, string? label = null)
	{
		uint index = (uint)Transaction.Outputs.Count();
		TxOut txOut = Transaction.Outputs.Add(new Money(amount, MoneyUnit.BTC), CreateScript(label));
		return new ForeignOutput(Transaction, index);
	}

	public WalletOutput AddWalletOutput(decimal amount = 1, string? label = null)
	{
		ForeignOutput foreignOutput = AddForeignOutput(amount, label);
		SmartCoin smartCoin = new(this, foreignOutput.Index, CreateHdPubKey(label));
		AddWalletOutput(smartCoin);
		return new WalletOutput(smartCoin);
	}

	public void AnalyzeRecursively()
	{
		var analyser = ServiceFactory.CreateBlockchainAnalyzer();
		HashSet<SmartTransaction> analyzedTransactions = new();

		// Analyze transactions in topological sorting
		void AnalyzeRecursivelyHelper(SmartTransaction transaction)
		{
			if (!analyzedTransactions.Contains(transaction))
			{
				analyzedTransactions.Add(transaction);
				foreach (SmartCoin walletInput in transaction.WalletInputs)
					AnalyzeRecursivelyHelper(walletInput.Transaction);
				analyser.Analyze(transaction);
			}
		}

		AnalyzeRecursivelyHelper(this);
	}
}

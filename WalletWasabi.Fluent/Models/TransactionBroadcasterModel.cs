using NBitcoin;
using System.Linq;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Extensions;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Models;

namespace WalletWasabi.Fluent.Models;

[AutoInterface]
public partial class TransactionBroadcasterModel
{
	private readonly Network _network;

	public TransactionBroadcasterModel(Network network)
	{
		_network = network;
	}

	public SmartTransaction? Parse(string text)
	{
		if (PSBT.TryParse(text, _network, out var signedPsbt))
		{
			if (!signedPsbt.IsAllFinalized())
			{
				signedPsbt.Finalize();
			}

			return signedPsbt.ExtractSmartTransaction();
		}
		else
		{
			return new SmartTransaction(Transaction.Parse(text, _network), Height.Unknown);
		}
	}

	public Task<SmartTransaction> LoadFromFileAsync(string filePath)
	{
		return TransactionHelpers.ParseTransactionAsync(filePath, _network);
	}

	public TransactionBroadcastInfo GetBroadcastInfo(SmartTransaction transaction)
	{
		var nullMoney = new Money(-1L);
		var nullOutput = new TxOut(nullMoney, Script.Empty);

		var psbt = PSBT.FromTransaction(transaction.Transaction, _network);

		TxOut GetOutput(OutPoint outpoint) =>
			Services.BitcoinStore.TransactionStore.TryGetTransaction(outpoint.Hash, out var prevTxn)
				? prevTxn.Transaction.Outputs[outpoint.N]
				: nullOutput;

		var inputAddressAmount = psbt.Inputs
			.Select(x => x.PrevOut)
			.Select(GetOutput)
			.ToArray();

		var outputAddressAmount = psbt.Outputs
			.Select(x => x.GetCoin().TxOut)
			.ToArray();

		var psbtTxn = psbt.GetOriginalTransaction();

		var transactionId = psbtTxn.GetHash().ToString();

		var inputCount = inputAddressAmount.Length;
		var totalInputValue =
			inputAddressAmount.Any(x => x.Value == nullMoney)
			? null
			: inputAddressAmount.Select(x => x.Value).Sum();

		var inputAmountString =
			totalInputValue is null
			? "Unknown"
			: $"BTC {totalInputValue.ToFormattedString()}";

		var outputCount = outputAddressAmount.Length;

		var totalOutputValue =
			outputAddressAmount.Any(x => x.Value == nullMoney)
			? null
			: outputAddressAmount.Select(x => x.Value).Sum();

		var outputAmountString =
			totalOutputValue is null
			? "Unknown"
			: $"BTC {totalOutputValue.ToFormattedString()}";

		var networkFee = totalInputValue is null || totalOutputValue is null
			? null
			: totalInputValue - totalOutputValue;

		var feeString = networkFee.ToFeeDisplayUnitFormattedString();

		return new TransactionBroadcastInfo(transactionId, inputCount, outputCount, inputAmountString, outputAmountString, feeString);
	}

	public Task SendAsync(SmartTransaction transaction)
	{
		return Services.TransactionBroadcaster.SendTransactionAsync(transaction);
	}
}

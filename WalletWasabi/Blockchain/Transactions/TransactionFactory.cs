using NBitcoin;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Exceptions;
using WalletWasabi.Extensions;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Tor.Socks5.Exceptions;
using WalletWasabi.WebClients.PayJoin;

namespace WalletWasabi.Blockchain.Transactions;

public class TransactionFactory
{
	public TransactionFactory(Network network, KeyManager keyManager, ICoinsView coins, ITransactionStore transactionStore, string password = "")
	{
		Network = network;
		KeyManager = keyManager;
		Coins = coins;
		TransactionStore = transactionStore;
		Password = password;
	}

	public Network Network { get; }
	public KeyManager KeyManager { get; }
	public ICoinsView Coins { get; }
	private string Password { get; }
	private ITransactionStore TransactionStore { get; }

	public BuildTransactionResult BuildTransaction(
		TransactionParameters parameters,
		Func<LockTime>? lockTimeSelector = null,
		IPayjoinClient? payjoinClient = null)
	{
		lockTimeSelector ??= () => LockTime.Zero;

		var payments = parameters.PaymentIntent;
		long totalAmount = payments.TotalAmount.Satoshi;
		if (totalAmount is < 0 or > Constants.MaximumNumberOfSatoshis)
		{
			throw new ArgumentOutOfRangeException($"{nameof(payments)}.{nameof(payments.TotalAmount)} sum cannot be smaller than 0 or greater than {Constants.MaximumNumberOfSatoshis}.");
		}

		// Get allowed coins to spend.
		var availableCoinsView = Coins.Unspent();
		if (parameters.AllowDoubleSpend && parameters.AllowedInputs is not null)
		{
			var doubleSpends = new List<SmartCoin>();
			foreach (var input in parameters.AllowedInputs)
			{
				if (((CoinsRegistry)Coins).AsAllCoinsView().TryGetByOutPoint(input, out var coin)
					&& coin.SpenderTransaction is not null
					&& !coin.SpenderTransaction.Confirmed)
				{
					doubleSpends.Add(coin);
				}
			}
			availableCoinsView = new CoinsView(availableCoinsView.ToList().Concat(doubleSpends));
		}

		List<SmartCoin> allowedSmartCoinInputs = parameters.AllowUnconfirmed // Inputs that can be used to build the transaction.
				? availableCoinsView.ToList()
				: availableCoinsView.Confirmed().ToList();
		if (parameters.AllowedInputs is not null) // If allowedInputs are specified then select the coins from them.
		{
			if (!parameters.AllowedInputs.Any())
			{
				throw new ArgumentException($"{nameof(parameters.AllowedInputs)} is not null, but empty.");
			}

			allowedSmartCoinInputs = allowedSmartCoinInputs
				.Where(x => parameters.AllowedInputs.Any(y => y.Hash == x.TransactionId && y.N == x.Index))
				.ToList();

			// Add those that have the same script, because common ownership is already exposed.
			// But only if the user didn't click the "max" button. In this case he'd send more money than what he'd think.
			if (payments.ChangeStrategy != ChangeStrategy.AllRemainingCustom)
			{
				var allScripts = allowedSmartCoinInputs.Select(x => x.ScriptPubKey).ToHashSet();
				foreach (var coin in availableCoinsView.Where(x => !allowedSmartCoinInputs.Any(y => x.TransactionId == y.TransactionId && x.Index == y.Index)))
				{
					if (!(parameters.AllowUnconfirmed || coin.Confirmed))
					{
						continue;
					}

					if (allScripts.Contains(coin.ScriptPubKey))
					{
						allowedSmartCoinInputs.Add(coin);
					}
				}
			}
		}

		TransactionBuilder builder = Network.CreateTransactionBuilder();
		builder.SetCoinSelector(new SmartCoinSelector(allowedSmartCoinInputs));
		builder.AddCoins(allowedSmartCoinInputs.Select(c => c.Coin));
		builder.SetLockTime(lockTimeSelector());

		foreach (var request in payments.Requests.Where(x => x.Amount.Type == MoneyRequestType.Value))
		{
			var amountRequest = request.Amount;

			builder.Send(request.Destination, amountRequest.Amount);
			if (amountRequest.SubtractFee)
			{
				builder.SubtractFees();
			}
		}

		HdPubKey? changeHdPubKey;

		if (payments.TryGetCustomRequest(out DestinationRequest? customChange))
		{
			var changeScript = customChange.Destination.ScriptPubKey;
			KeyManager.TryGetKeyForScriptPubKey(changeScript, out HdPubKey? hdPubKey);
			changeHdPubKey = hdPubKey;

			var changeStrategy = payments.ChangeStrategy;
			if (changeStrategy == ChangeStrategy.Custom)
			{
				builder.SetChange(changeScript);
			}
			else if (changeStrategy == ChangeStrategy.AllRemainingCustom)
			{
				builder.SendAllRemaining(changeScript);
			}
			else
			{
				throw new NotSupportedException(payments.ChangeStrategy.ToString());
			}
		}
		else
		{
			changeHdPubKey = KeyManager.GetNextChangeKey();

			builder.SetChange(changeHdPubKey.GetAssumedScriptPubKey());
		}

		builder.OptInRBF = true;

		builder.SendEstimatedFees(parameters.FeeRate);

		var psbt = builder.BuildPSBT(false);

		var spentCoins = psbt.Inputs.Select(txin => allowedSmartCoinInputs.First(y => y.Outpoint == txin.PrevOut)).ToArray();

		var realToSend = payments.Requests
			.Select(t =>
				(label: t.Labels,
				destination: t.Destination,
				amount: psbt.Outputs.FirstOrDefault(o => o.ScriptPubKey == t.Destination.ScriptPubKey)?.Value))
			.Where(i => i.amount is not null);

		if (!psbt.TryGetFee(out var fee))
		{
			throw new InvalidOperationException("Impossible to get the fees of the PSBT, this should never happen.");
		}

		var vSize = builder.EstimateSize(psbt.GetOriginalTransaction(), true);

		// Do some checks
		Money totalSendAmountNoFee = realToSend.Sum(x => x.amount);
		if (totalSendAmountNoFee == Money.Zero)
		{
			throw new InvalidOperationException("The amount after subtracting the fee is too small to be sent.");
		}

		Money totalOutgoingAmountNoFee;
		if (changeHdPubKey is null)
		{
			totalOutgoingAmountNoFee = totalSendAmountNoFee;
		}
		else
		{
			totalOutgoingAmountNoFee = realToSend.Where(x => !changeHdPubKey.ContainsScript(x.destination.ScriptPubKey)).Sum(x => x.amount);
		}

		decimal totalOutgoingAmountNoFeeDecimal = totalOutgoingAmountNoFee.ToDecimal(MoneyUnit.BTC);
		decimal feeDecimal = fee.ToDecimal(MoneyUnit.BTC);

		decimal feePercentage;
		if (payments.ChangeStrategy == ChangeStrategy.AllRemainingCustom)
		{
			// In this scenario since the amount changes as the fee changes, we need to compare against the total sum / 2,
			// as with this, we will make sure the fee cannot be higher than the amount.
			decimal inputSumDecimal = spentCoins.Sum(x => x.Amount.ToDecimal(MoneyUnit.BTC));
			feePercentage = 100 * (feeDecimal / (inputSumDecimal / 2));
		}
		else
		{
			// In this scenario the amount is fixed, so we can compare against it.
			// Cannot divide by zero, so use the closest number we have to zero.
			decimal totalOutgoingAmountNoFeeDecimalDivisor = totalOutgoingAmountNoFeeDecimal == 0 ? decimal.MinValue : totalOutgoingAmountNoFeeDecimal;
			feePercentage = 100 * (feeDecimal / totalOutgoingAmountNoFeeDecimalDivisor);
		}
		if (feePercentage > 100 && !parameters.OverrideFeeOverpaymentProtection)
		{
			throw new TransactionFeeOverpaymentException(feePercentage);
		}

		// Build the transaction

		// It must be watch only, too, because if we have the key and also hardware wallet, we do not care we can sign.
		psbt.AddKeyPaths(KeyManager);
		psbt.AddPrevTxs(TransactionStore);

		Transaction tx;
		if (KeyManager.IsWatchOnly || !parameters.TryToSign)
		{
			tx = psbt.GetGlobalTransaction();
		}
		else
		{
			IEnumerable<ExtKey> signingKeys = KeyManager.GetSecrets(Password, spentCoins.Select(x => x.ScriptPubKey).ToArray());
			builder = builder.AddKeys(signingKeys.ToArray());
			builder.SignPSBT(psbt);

			// Try to pay using payjoin
			if (payjoinClient is not null)
			{
				psbt = TryNegotiatePayjoin(payjoinClient, builder, psbt, changeHdPubKey);
				psbt.AddKeyPaths(KeyManager);
				psbt.AddPrevTxs(TransactionStore);
			}

			psbt.Finalize();
			tx = psbt.ExtractTransaction();

			if (payjoinClient is not null)
			{
				builder.CoinFinder = (outpoint) => psbt.Inputs.Select(x => x.GetCoin()).Single(x => x?.Outpoint == outpoint)!;
			}

			var checkResults = builder.Check(tx).ToList();
			if (checkResults.Count > 0)
			{
				Logger.LogDebug($"Found policy error(s)! First error: '{checkResults[0]}'.");
				throw new InvalidTxException(tx, checkResults);
			}
		}

		var smartTransaction = new SmartTransaction(tx, Height.Unknown, labels: LabelsArray.Merge(payments.Requests.Select(x => x.Labels)));
		foreach (var coin in spentCoins)
		{
			smartTransaction.TryAddWalletInput(coin);
		}
		var label = LabelsArray.Merge(payments.Requests.Select(x => x.Labels).Concat(smartTransaction.WalletInputs.Select(x => x.HdPubKey.Labels)));

		for (var i = 0U; i < tx.Outputs.Count; i++)
		{
			TxOut output = tx.Outputs[i];
			if (KeyManager.TryGetKeyForScriptPubKey(output.ScriptPubKey, out HdPubKey? foundKey))
			{
				var smartCoin = new SmartCoin(smartTransaction, i, foundKey);
				label = LabelsArray.Merge(label, smartCoin.HdPubKey.Labels); // foundKey's label is already added to the coinLabel.
				smartTransaction.TryAddWalletOutput(smartCoin);
			}
		}

		// New labels will be added to the HdPubKey only when tx will be successfully broadcasted.
		Dictionary<HdPubKey, LabelsArray> hdPubKeysWithNewLabels = new();

		foreach (var coin in smartTransaction.WalletOutputs)
		{
			var foundPaymentRequest = payments.Requests.FirstOrDefault(x => x.Destination.ScriptPubKey == coin.ScriptPubKey);

			// If change then we concatenate all the labels.
			// The foundKeyLabel has already been added previously, so no need to concatenate.
			if (foundPaymentRequest is null) // Then it's auto-change.
			{
				hdPubKeysWithNewLabels.Add(coin.HdPubKey, label);
			}
			else
			{
				hdPubKeysWithNewLabels.Add(coin.HdPubKey, LabelsArray.Merge(coin.HdPubKey.Labels, foundPaymentRequest.Labels));
			}
		}

		var sign = !KeyManager.IsWatchOnly;

		Logger.LogDebug($"Built tx: BTC {totalOutgoingAmountNoFee.ToString(fplus: false, trimExcessZero: true)}. Fee: {fee.Satoshi} sats. Vsize: {vSize} vBytes. Fee/Total ratio: {feePercentage:0.#}%. Tx hash: {tx.GetHash()}.");
		return new BuildTransactionResult(smartTransaction, psbt, sign, fee, feePercentage, hdPubKeysWithNewLabels);
	}

	private PSBT TryNegotiatePayjoin(IPayjoinClient payjoinClient, TransactionBuilder builder, PSBT psbt, HdPubKey changeHdPubKey)
	{
		try
		{
			Logger.LogInfo($"Negotiating payjoin payment with `{payjoinClient.PaymentUrl}`.");

			psbt = payjoinClient.RequestPayjoin(
				psbt,
				KeyManager.SegwitExtPubKey,
				new RootedKeyPath(KeyManager.MasterFingerprint.Value, KeyManager.SegwitAccountKeyPath),
				changeHdPubKey,
				CancellationToken.None).GetAwaiter().GetResult(); // WTF??!
			builder.SignPSBT(psbt);

			Logger.LogInfo("Payjoin payment was negotiated successfully.");
		}
		catch (HttpRequestException ex) when (ex.InnerException is TorConnectCommandFailedException innerEx)
		{
			if (innerEx.Message.Contains("HostUnreachable"))
			{
				Logger.LogWarning("Payjoin server is not reachable. Ignoring...");
			}

			// Ignore.
		}
		catch (HttpRequestException e)
		{
			Logger.LogWarning($"Payjoin server responded with {e.ToTypeMessageString()}. Ignoring...");
		}
		catch (PayjoinException e)
		{
			Logger.LogWarning($"Payjoin server responded with {e.Message}. Ignoring...");
		}

		return psbt;
	}
}

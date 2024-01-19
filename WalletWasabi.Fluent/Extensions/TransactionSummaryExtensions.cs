using System.Diagnostics.CodeAnalysis;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.Extensions;

public static class TransactionSummaryExtensions
{
	public static bool IsConfirmed(this TransactionSummary model)
	{
		var confirmations = model.GetConfirmations();
		return confirmations > 0;
	}

	public static int GetConfirmations(this TransactionSummary model)
		=> model.Transaction.GetConfirmations((int)Services.SmartHeaderChain.ServerTipHeight);

	public static bool TryGetConfirmationTime(this TransactionSummary model, [NotNullWhen(true)] out TimeSpan? estimate)
		=> TransactionFeeHelper.TryEstimateConfirmationTime(Services.HostedServices.Get<HybridFeeProvider>(), Services.WalletManager.Network, model.Transaction, Services.HostedServices.Get<TransactionFeeProvider>(), out estimate);
}

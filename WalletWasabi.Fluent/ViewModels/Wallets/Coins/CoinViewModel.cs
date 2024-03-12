using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.ViewModels.CoinControl.Core;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Coins;

public class CoinViewModel : CoinListItem
{
	public CoinViewModel(LabelsArray labels, ICoinModel coin)
	{
		Labels = labels;
		Coin = coin;
		Amount = coin.Amount;
		IsConfirmed = coin.IsConfirmed;
		IsBanned = coin.IsBanned;
		IsCoinjoining = coin.IsCoinJoinInProgress;
		var confirmationCount = coin.Confirmations;
		ConfirmationStatus = $"{confirmationCount} confirmation{TextHelpers.AddSIfPlural(confirmationCount)}";
		BannedUntilUtcToolTip = coin.BannedUntilUtcToolTip;
		AnonymityScore = coin.AnonScore;
		BannedUntilUtc = coin.BannedUntilUtc;
		IsSelected = false;
		ScriptType = coin.ScriptType;
	}

	public ICoinModel Coin { get; }
}

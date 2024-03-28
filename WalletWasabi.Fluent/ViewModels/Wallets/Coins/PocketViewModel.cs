using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData;
using ReactiveUI;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Fluent.Models;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.ViewModels.CoinControl.Core;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Coins;

public class PocketViewModel : CoinListItem, IDisposable
{
	private readonly CompositeDisposable _disposables = new();

	public PocketViewModel(IWalletModel wallet, Pocket pocket)
	{
		var pocketCoins = pocket.Coins.ToList();

		var unconfirmedCount = pocketCoins.Count(x => !x.Confirmed);
		IsConfirmed = unconfirmedCount == 0;
		ConfirmationStatus = IsConfirmed ? "All coins are confirmed" : $"{unconfirmedCount} coins are waiting for confirmation";
		IsBanned = pocketCoins.Any(x => x.IsBanned);
		BannedUntilUtcToolTip = IsBanned ? "Some coins can't participate in coinjoin" : null;
		Amount = pocket.Amount;
		IsCoinjoining = pocketCoins.Any(x => x.CoinJoinInProgress);
		AnonymityScore = GetAnonScore(pocketCoins);
		Labels = pocket.Labels;
		Children =
			pocketCoins.Select(wallet.Coins.GetCoinModel)
					   .OrderByDescending(x => x.AnonScore)
					   .Select(coin => new CoinViewModel("", coin) { IsChild = true })
					   .ToList();

		CanBeSelected = true;
		ScriptType = null;

		Children
			.AsObservableChangeSet()
			.WhenPropertyChanged(x => x.IsSelected)
			.Select(c => Children.Where(x => x.Coin.IsSameAddress(c.Sender.Coin) && x.IsSelected != c.Sender.IsSelected))
			.Do(coins =>
			{
				// Select/deselect all the coins on the same address.
				foreach (var coin in coins)
				{
					coin.IsSelected = !coin.IsSelected;
				}
			})
			.Select(_ =>
			{
				var totalCount = Children.Count;
				var selectedCount = Children.Count(x => x.IsSelected == true);
				return (bool?)(selectedCount == totalCount ? true : selectedCount == 0 ? false : null);
			})
			.BindTo(this, x => x.IsSelected)
			.DisposeWith(_disposables);

		this.WhenAnyValue(x => x.IsSelected)
			.Do(isSelected =>
			{
				if (isSelected is null)
				{
					return;
				}

				foreach (var item in Children)
				{
					item.IsSelected = isSelected.Value;
				}
			})
			.Subscribe()
			.DisposeWith(_disposables);
	}

	public void Dispose()
	{
		_disposables.Dispose();
	}

	private static int? GetAnonScore(IEnumerable<SmartCoin> pocketCoins)
	{
		var allScores = pocketCoins.Select(x => (int?)x.AnonymitySet);
		return CommonOrDefault(allScores.ToList());
	}

	/// <summary>
	/// Returns the common item in the list, if any.
	/// </summary>
	/// <typeparam name="T">Type of the item</typeparam>
	/// <param name="list">List of items to determine the common item.</param>
	/// <returns>The common item or <c>default</c> if there is no common item.</returns>
	private static T? CommonOrDefault<T>(IList<T> list)
	{
		var commonItem = list[0];

		for (var i = 1; i < list.Count; i++)
		{
			if (!Equals(list[i], commonItem))
			{
				return default;
			}
		}

		return commonItem;
	}
}

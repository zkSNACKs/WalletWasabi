using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Send;

public partial class LabelSelectionViewModel : ViewModelBase
{
	private readonly KeyManager _keyManager;
	private readonly string _password;
	private readonly TransactionInfo _info;
	private readonly bool _isSilent;
	private readonly List<Pocket> _hiddenIncludedPockets = new();

	[AutoNotify] private bool _enoughSelected;

	private Pocket _privatePocket = Pocket.Empty;
	private Pocket _semiPrivatePocket = Pocket.Empty;
	private Pocket[] _allPockets = Array.Empty<Pocket>();

	public LabelSelectionViewModel(KeyManager keyManager, string password, TransactionInfo info, bool isSilent)
	{
		_keyManager = keyManager;
		_password = password;
		_info = info;
		_isSilent = isSilent;
	}

	public Pocket[] NonPrivatePockets { get; set; } = Array.Empty<Pocket>();

	public IEnumerable<LabelViewModel> AllLabelsViewModel { get; set; } = Array.Empty<LabelViewModel>();

	public IEnumerable<LabelViewModel> LabelsWhiteList => AllLabelsViewModel.Where(x => !x.IsBlackListed);

	public IEnumerable<LabelViewModel> LabelsBlackList => AllLabelsViewModel.Where(x => x.IsBlackListed);

	private async Task<bool> IsPocketEnoughAsync(params Pocket[] pockets)
	{
		var coins = Pocket.Merge(pockets).Coins;
		var allCoins = Pocket.Merge(_allPockets).Coins;

		return await Task.Run(() => TransactionHelpers.TryBuildTransactionWithoutPrevTx(
			keyManager: _keyManager,
			transactionInfo: _info,
			allCoins: new CoinsView(allCoins),
			allowedCoins: coins,
			password: _password,
			minimumAmount: out _));
	}

	public async Task<Pocket[]> AutoSelectPocketsAsync()
	{
		var privateAndSemiPrivatePockets = new[] { _privatePocket, _semiPrivatePocket };

		var knownPockets = NonPrivatePockets.Where(x => x.Labels != CoinPocketHelper.UnlabelledFundsText).ToArray();
		var unknownPockets = NonPrivatePockets.Except(knownPockets).ToArray();

		var privateAndSemiPrivateAndUnknownPockets = privateAndSemiPrivatePockets.Union(unknownPockets).ToArray();
		var privateAndSemiPrivateAndKnownPockets = privateAndSemiPrivatePockets.Union(knownPockets).ToArray();

		var knownByRecipientPockets = knownPockets.Where(pocket => pocket.Labels.Any(label => _info.Recipient.Contains(label, StringComparer.OrdinalIgnoreCase))).ToArray();
		var onlyKnownByRecipientPockets = knownByRecipientPockets.Where(pocket => pocket.Labels.Equals(_info.Recipient, StringComparer.OrdinalIgnoreCase)).ToArray();

		if (await IsPocketEnoughAsync(onlyKnownByRecipientPockets))
		{
			return onlyKnownByRecipientPockets;
		}

		if (await IsPocketEnoughAsync(_privatePocket))
		{
			return new[] { _privatePocket };
		}

		if (await IsPocketEnoughAsync(privateAndSemiPrivatePockets))
		{
			return privateAndSemiPrivatePockets;
		}

		var (result, pockets) = await TryGetBestKnownByRecipientPocketsWithPrivateAndSemiPrivatePocketsAsync(knownByRecipientPockets, privateAndSemiPrivatePockets, _info.Recipient);
		if (result)
		{
			return pockets;
		}

		if (await IsPocketEnoughAsync(privateAndSemiPrivateAndKnownPockets))
		{
			return privateAndSemiPrivateAndKnownPockets;
		}

		if (await IsPocketEnoughAsync(privateAndSemiPrivateAndUnknownPockets))
		{
			return privateAndSemiPrivateAndUnknownPockets;
		}

		return _allPockets.ToArray();
	}

	private async Task<(bool, Pocket[] pockets)> TryGetBestKnownByRecipientPocketsWithPrivateAndSemiPrivatePocketsAsync(Pocket[] knownByRecipientPockets, Pocket[] privateAndSemiPrivatePockets, LabelsArray recipient)
	{
		if (!await IsPocketEnoughAsync(Pocket.Merge(knownByRecipientPockets, privateAndSemiPrivatePockets)))
		{
			return (false, Array.Empty<Pocket>());
		}

		var privacyRankedPockets =
			knownByRecipientPockets
				.Select(pocket =>
				{
					var containedRecipientLabelsCount = pocket.Labels.Count(label => recipient.Contains(label, StringComparer.OrdinalIgnoreCase));
					var totalPocketLabelsCount = pocket.Labels.Count;
					var totalRecipientLabelsCount = recipient.Count;
					var index = ((double)containedRecipientLabelsCount / totalPocketLabelsCount) + ((double)containedRecipientLabelsCount / totalRecipientLabelsCount);

					return (acceptabilityIndex: index, pocket);
				})
				.OrderByDescending(tup => tup.acceptabilityIndex)
				.ThenBy(tup => tup.pocket.Labels.Count)
				.ThenByDescending(tup => tup.pocket.Amount)
				.Select(tup => tup.pocket)
				.ToArray();

		var bestPockets = new List<Pocket>();
		if (Pocket.Merge(privateAndSemiPrivatePockets).Coins.TotalAmount() != Money.Zero)
		{
			bestPockets.AddRange(privateAndSemiPrivatePockets);
		}

		// Iterate through the ordered by privacy pockets and add them one by one until the total amount covers the payment.
		// The first one is the best from the privacy point of view, and the last one is the worst.
		foreach (var p in privacyRankedPockets)
		{
			bestPockets.Add(p);

			if (await IsPocketEnoughAsync(bestPockets.ToArray()))
			{
				break;
			}
		}

		// It can happen that there are unnecessary selected pockets, so remove the ones that are not needed.
		// Use Except to make sure the private and semi private pockets never get removed.
		// Example for an over selection:
		// Privacy ordered pockets: [A - 3 BTC] [B - 1 BTC] [C - 2 BTC] (A is the best for privacy, C is the worst)
		// Target amount is 4.5 BTC so the algorithm will select all because it happened in privacy order.
		// But B is unnecessary because A and C can cover the case, so remove it.
		foreach (var p in bestPockets.Except(privateAndSemiPrivatePockets).OrderBy(x => x.Amount).ThenByDescending(x => x.Labels.Count).ToImmutableArray())
		{
			if (await IsPocketEnoughAsync(bestPockets.Except(new[] { p }).ToArray()))
			{
				bestPockets.Remove(p);
			}
			else
			{
				break;
			}
		}

		return (true, bestPockets.ToArray());
	}

	public Pocket[] GetUsedPockets() =>
		NonPrivatePockets.Where(x => x.Labels.All(label => LabelsWhiteList.Any(labelViewModel => labelViewModel.Value == label)))
			.Union(_hiddenIncludedPockets)
			.ToArray();

	public async Task ResetAsync(Pocket[] pockets, List<SmartCoin>? coinsToExclude = null)
	{
		_allPockets = pockets;

		if (coinsToExclude is not null)
		{
			var pocketsWithoutExcludedCoins = _allPockets.Select(x => new Pocket((x.Labels, new CoinsView(x.Coins.Except(coinsToExclude))))).ToArray();

			if (await IsPocketEnoughAsync(pocketsWithoutExcludedCoins))
			{
				_allPockets = pocketsWithoutExcludedCoins;
			}
		}

		if (_allPockets.FirstOrDefault(x => x.Labels == CoinPocketHelper.PrivateFundsText) is { } privatePocket)
		{
			_privatePocket = privatePocket;
		}

		if (_allPockets.FirstOrDefault(x => x.Labels == CoinPocketHelper.SemiPrivateFundsText) is { } semiPrivatePocket)
		{
			_semiPrivatePocket = semiPrivatePocket;
		}

		NonPrivatePockets = _allPockets.Where(x => x != _privatePocket && x != _semiPrivatePocket).ToArray();

		var allLabels = LabelsArray.Merge(NonPrivatePockets.Select(x => x.Labels));
		AllLabelsViewModel = allLabels.Select(x => new LabelViewModel(this, x)).ToArray();

		if (AllLabelsViewModel.FirstOrDefault(x => x.Value == CoinPocketHelper.UnlabelledFundsText) is { } unlabelledViewModel)
		{
			unlabelledViewModel.IsDangerous = true;
			unlabelledViewModel.ToolTip = "There is no information about these people, only use it when necessary!";
		}

		if (!_isSilent)
		{
			await OnSelectionChangedAsync();
		}
	}

	public LabelViewModel[] GetAssociatedLabels(LabelViewModel labelViewModel)
	{
		if (labelViewModel.IsBlackListed)
		{
			var associatedPocketLabels = NonPrivatePockets.OrderBy(x => x.Labels.Count).First(x => x.Labels.Contains(labelViewModel.Value)).Labels;
			return LabelsBlackList.Where(x => associatedPocketLabels.Contains(x.Value)).ToArray();
		}
		else
		{
			var associatedPockets = NonPrivatePockets.Where(x => x.Labels.Contains(labelViewModel.Value));
			var notAssociatedPockets = NonPrivatePockets.Except(associatedPockets);
			var allNotAssociatedLabels = LabelsArray.Merge(notAssociatedPockets.Select(x => x.Labels));
			return LabelsWhiteList.Where(x => !allNotAssociatedLabels.Contains(x.Value)).ToArray();
		}
	}

	public void OnFade(LabelViewModel source)
	{
		foreach (var lvm in source.IsBlackListed ? LabelsBlackList : LabelsWhiteList)
		{
			if (!lvm.IsHighlighted)
			{
				lvm.Fade(source);
			}
		}
	}

	public void OnPointerOver(LabelViewModel labelViewModel)
	{
		var affectedLabelViewModels = GetAssociatedLabels(labelViewModel);

		foreach (var lvm in affectedLabelViewModels)
		{
			lvm.Highlight(triggerSource: labelViewModel);
		}
	}

	public async Task SwapLabelAsync(LabelViewModel labelViewModel)
	{
		var affectedLabelViewModels = GetAssociatedLabels(labelViewModel);

		foreach (var lvm in affectedLabelViewModels)
		{
			lvm.Swap();
		}

		await OnSelectionChangedAsync();
	}

	private async Task OnSelectionChangedAsync()
	{
		_hiddenIncludedPockets.Clear();

		var (isPrivateNeeded, isSemiPrivateNeeded) = await ArePrivateAndSemiPrivatePocketsNeededAsync();

		if (isPrivateNeeded)
		{
			_hiddenIncludedPockets.Add(_privatePocket);
		}

		if (isSemiPrivateNeeded)
		{
			_hiddenIncludedPockets.Add(_semiPrivatePocket);
		}

		EnoughSelected = await IsPocketEnoughAsync(GetUsedPockets());

		this.RaisePropertyChanged(nameof(LabelsWhiteList));
		this.RaisePropertyChanged(nameof(LabelsBlackList));
	}

	private async Task<(bool, bool)> ArePrivateAndSemiPrivatePocketsNeededAsync()
	{
		var isPrivateNeeded = false;
		var isSemiPrivateNeeded = false;

		var usedPockets = GetUsedPockets();
		var usedPocketsLabels = new LabelsArray(usedPockets.SelectMany(p => p.Labels));

		if (usedPocketsLabels != _info.Recipient || !await IsPocketEnoughAsync(usedPockets))
		{
			isPrivateNeeded = true;

			if (!await IsPocketEnoughAsync(Pocket.Merge(usedPockets, _privatePocket)))
			{
				isSemiPrivateNeeded = true;
			}
		}

		return (isPrivateNeeded, isSemiPrivateNeeded);
	}

	public async Task SetUsedLabelAsync(IEnumerable<SmartCoin>? usedCoins, int privateThreshold)
	{
		if (usedCoins is null)
		{
			return;
		}

		usedCoins = usedCoins.ToImmutableArray();

		var usedLabels = LabelsArray.Merge(usedCoins.Select(x => x.GetLabels(privateThreshold)));
		var usedLabelViewModels = AllLabelsViewModel.Where(x => usedLabels.Contains(x.Value, StringComparer.OrdinalIgnoreCase)).ToArray();
		var notUsedLabelViewModels = AllLabelsViewModel.Except(usedLabelViewModels);

		foreach (LabelViewModel label in notUsedLabelViewModels)
		{
			label.Swap();
		}

		await OnSelectionChangedAsync();
	}

	public bool IsOtherSelectionPossible(IEnumerable<SmartCoin> usedCoins, LabelsArray recipient)
	{
		var usedPockets = _allPockets.Where(pocket => pocket.Coins.Any(usedCoins.Contains)).ToImmutableArray();
		var remainingUsablePockets = _allPockets.Except(usedPockets).ToList();

		// They are handled silently, do not take them into account as manually selectable pockets.
		remainingUsablePockets.Remove(_privatePocket);
		remainingUsablePockets.Remove(_semiPrivatePocket);

		if (usedPockets.Length == 1)
		{
			return false;
		}

		if (usedPockets.Length == 2 && usedPockets.Contains(_privatePocket) && usedPockets.Contains(_semiPrivatePocket))
		{
			return false;
		}

		if (remainingUsablePockets.Count == 0)
		{
			return false;
		}

		var labels = LabelsArray.Merge(usedPockets.Select(x => x.Labels));
		if (labels.Equals(recipient, StringComparer.OrdinalIgnoreCase))
		{
			return false;
		}

		return true;
	}
}

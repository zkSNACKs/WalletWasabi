using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Receive
{
	[NavigationMetaData(
		Title = "Receive",
		Caption = "",
		IconName = "wallet_action_receive",
		NavBarPosition = NavBarPosition.None,
		Searchable = false,
		NavigationTarget = NavigationTarget.DialogScreen)]
	public partial class ReceiveViewModel : RoutableViewModel
	{
		private readonly Wallet _wallet;
		private readonly SourceList<SuggestionLabelViewModel> _suggestionLabels;
		private readonly ObservableCollectionExtended<SuggestionLabelViewModel> _suggestionLabelResults;
		private readonly ObservableCollectionExtended<string> _labels;
		[AutoNotify] private HashSet<string> _suggestions;
		[AutoNotify] private bool _isExistingAddressesButtonVisible;

		public ReceiveViewModel(Wallet wallet)
		{
			_wallet = wallet;
			_labels = new ObservableCollectionExtended<string>();
			var allLabels = WalletHelpers.GetLabels();

			var mostUsedLabels = allLabels.GroupBy(x => x)
				.Select(x => new
				{
					Label = x.Key,
					Count = x.Count()
				})
				.OrderByDescending(x => x.Count)
				.ToList();

			_suggestions = mostUsedLabels.Select(x => x.Label).ToHashSet();

			_suggestionLabels = new SourceList<SuggestionLabelViewModel>();
			_suggestionLabelResults = new ObservableCollectionExtended<SuggestionLabelViewModel>();

			_suggestionLabels.AddRange(
				mostUsedLabels.Select(x => new SuggestionLabelViewModel(x.Label, x.Count)));

			var suggestionLabelsFilter = this.WhenAnyValue(x => x.Labels).Select(_ => Unit.Default)
				.Merge(Observable.FromEventPattern(Labels, nameof(Labels.CollectionChanged)).Select(_ => Unit.Default))
				.Select(SuggestionLabelsFilter);

			_suggestionLabels
				.Connect()
				.Filter(suggestionLabelsFilter)
				.Sort(SortExpressionComparer<SuggestionLabelViewModel>.Descending(x => x.Count))
				.Top(3)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Bind(_suggestionLabelResults)
				.Subscribe();

			SetupCancel(enableCancel: true, enableCancelOnEscape: true, enableCancelOnPressed: true);

			EnableBack = false;

			NextCommand = ReactiveCommand.Create(OnNext, _labels.ToObservableChangeSet().Select(_ => _labels.Count > 0));

			ShowExistingAddressesCommand = ReactiveCommand.Create(OnShowExistingAddresses);
		}

		public ICommand ShowExistingAddressesCommand { get; }

		public ObservableCollection<SuggestionLabelViewModel> SuggestionLabelResults => _suggestionLabelResults;

		public ObservableCollection<string> Labels => _labels;

		private Func<SuggestionLabelViewModel, bool> SuggestionLabelsFilter(Unit unit)
		{
			return suggestionLabel => !_labels.Contains(suggestionLabel.Label);
		}

		private void OnNext()
		{
			var newKey = _wallet.KeyManager.GetNextReceiveKey(new SmartLabel(Labels), out bool minGapLimitIncreased);

			if (minGapLimitIncreased)
			{
				int minGapLimit = _wallet.KeyManager.MinGapLimit.Value;
				int prevMinGapLimit = minGapLimit - 1;
				var minGapLimitMessage = $"Minimum gap limit increased from {prevMinGapLimit} to {minGapLimit}.";

				// TODO: notification
			}

			Labels.Clear();

			Navigate().To(new ReceiveAddressViewModel(_wallet, newKey));
		}

		private void OnShowExistingAddresses()
		{
			Navigate().To(new ReceiveAddressesViewModel(_wallet, Suggestions));
		}

		protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposable)
		{
			base.OnNavigatedTo(isInHistory, disposable);

			IsExistingAddressesButtonVisible = _wallet.KeyManager.GetKeys(x => !x.Label.IsEmpty && !x.IsInternal && x.KeyState == KeyState.Clean).Any();
		}
	}
}

using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Infrastructure;
using WalletWasabi.Fluent.ViewModels.SearchBar.SearchItems;
using WalletWasabi.Fluent.ViewModels.SearchBar.Sources;

namespace WalletWasabi.Fluent.ViewModels.SearchBar;

[AppLifetime]
public partial class SearchBarViewModel : ReactiveObject
{
	private readonly ReadOnlyObservableCollection<SearchItemGroup> _groups;
	[AutoNotify] private string _searchText = "";

	public SearchBarViewModel(ISearchSource searchSource)
	{
		searchSource.Changes
            .DisposeMany()
			.Group(s => s.Category)
			.Transform(group => new SearchItemGroup(group.Key, group.Cache.Connect()))
			.Sort(SortExpressionComparer<SearchItemGroup>.Ascending(x => x.Title))
			.Bind(out _groups)
			.DisposeMany()
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe();

		var activateFirstItemCommand = ReactiveCommand.Create(
			() =>
			{
				if (_groups is [{ Items: [IActionableItem item] }])
				{
					item.Activate();
					SearchText = "";
				}
			});
		
		ActivateFirstItemCommand = activateFirstItemCommand;
		CommandActivated = activateFirstItemCommand.ToSignal();
		ResetCommand = ReactiveCommand.Create(() => SearchText = "");
	}

	public IObservable<Unit> CommandActivated { get; }

	public ICommand ResetCommand { get; }

	public ICommand ActivateFirstItemCommand { get; set; }

	public ReadOnlyObservableCollection<SearchItemGroup> Groups => _groups;
}

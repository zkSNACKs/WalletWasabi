using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using WalletWasabi.Fluent.Infrastructure;
using WalletWasabi.Fluent.ViewModels.SearchBar.Patterns;
using WalletWasabi.Fluent.ViewModels.SearchBar.SearchItems;

namespace WalletWasabi.Fluent.ViewModels.SearchBar.Sources;

[AppLifetime]
public class EditableSearchSourceSource : IEditableSearchSource
{
	private readonly SourceCache<ISearchItem, ComposedKey> _actions = new(item => item.Key);
	private readonly ISubject<string> _queriesSubject = new Subject<string>();

	public EditableSearchSourceSource()
	{
		var filter = _queriesSubject.Select(SearchSource.DefaultFilter);

		Changes = _actions
			.Connect()
			.Filter(filter);
	}

	public IObservable<IChangeSet<ISearchItem, ComposedKey>> Changes { get; }

	public void SetQueries(IObservable<string> queries)
	{
		queries.Subscribe(_queriesSubject);
	}

	public void Remove(params ISearchItem[] searchItems)
	{
		foreach (var searchItem in searchItems)
		{
			_actions.Remove(searchItem);
		}
	}

	public void Add(params ISearchItem[] searchItems)
	{
		foreach (var searchItem in searchItems)
		{
			_actions.AddOrUpdate(searchItem);
		}
	}
}

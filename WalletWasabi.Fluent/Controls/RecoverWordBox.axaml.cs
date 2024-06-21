using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Metadata;
using Avalonia.Threading;
using ReactiveUI;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;

namespace WalletWasabi.Fluent.Controls;

public class RecoverWordBox : TemplatedControl
{
	private CompositeDisposable? _compositeDisposable;
	private TagsBoxAutoCompleteBox? _autoCompleteBox;
	private StringComparison _stringComparison;
	private bool _isInputEnabled = true;
	private IList<string>? _suggestions;
	private IEnumerable<string>? _items;
	private bool _requestAdd;

	public static readonly StyledProperty<string?> TextProperty =
		TextBlock.TextProperty.AddOwner<RecoverWordBox>(
			new StyledPropertyMetadata<string?>(
				string.Empty,
				BindingMode.TwoWay,
				enableDataValidation: true));

	public static readonly StyledProperty<bool> IsCurrentTextValidProperty =
		AvaloniaProperty.Register<RecoverWordBox, bool>(nameof(IsCurrentTextValid));

	public static readonly DirectProperty<RecoverWordBox, bool> RequestAddProperty =
		AvaloniaProperty.RegisterDirect<RecoverWordBox, bool>(nameof(RequestAdd), o => o.RequestAdd);

	public static readonly StyledProperty<bool> ForceAddProperty =
		AvaloniaProperty.Register<RecoverWordBox, bool>(nameof(ForceAdd));

	public static readonly StyledProperty<bool> RestrictInputToSuggestionsProperty =
		AvaloniaProperty.Register<RecoverWordBox, bool>(nameof(RestrictInputToSuggestions));

	public static readonly StyledProperty<int> ItemCountLimitProperty =
		AvaloniaProperty.Register<RecoverWordBox, int>(nameof(ItemCountLimit));

	public static readonly StyledProperty<int> MaxTextLengthProperty =
		AvaloniaProperty.Register<RecoverWordBox, int>(nameof(MaxTextLength));

	public static readonly StyledProperty<char> TagSeparatorProperty =
		AvaloniaProperty.Register<RecoverWordBox, char>(nameof(TagSeparator), defaultValue: ' ');

	public static readonly StyledProperty<bool> SuggestionsAreCaseSensitiveProperty =
		AvaloniaProperty.Register<RecoverWordBox, bool>(nameof(SuggestionsAreCaseSensitive), defaultValue: true);

	public static readonly StyledProperty<bool> AllowDuplicationProperty =
		AvaloniaProperty.Register<RecoverWordBox, bool>(nameof(AllowDuplication));

	public static readonly DirectProperty<RecoverWordBox, IEnumerable<string>?> ItemsProperty =
		AvaloniaProperty.RegisterDirect<RecoverWordBox, IEnumerable<string>?>(nameof(Items),
			o => o.Items,
			(o, v) => o.Items = v,
			enableDataValidation: true);

	public static readonly DirectProperty<RecoverWordBox, IList<string>?> SuggestionsProperty =
		AvaloniaProperty.RegisterDirect<RecoverWordBox, IList<string>?>(
			nameof(Suggestions),
			o => o.Suggestions,
			(o, v) => o.Suggestions = v);

	public static readonly StyledProperty<bool> IsReadOnlyProperty =
		AvaloniaProperty.Register<RecoverWordBox, bool>(nameof(IsReadOnly));

	public static readonly StyledProperty<bool> EnableCounterProperty =
		AvaloniaProperty.Register<RecoverWordBox, bool>(nameof(EnableCounter));

	public static readonly StyledProperty<bool> EnableDeleteProperty =
		AvaloniaProperty.Register<RecoverWordBox, bool>(nameof(EnableDelete), true);

	public static readonly StyledProperty<bool> IsSelectedProperty =
		AvaloniaProperty.Register<RecoverWordBox, bool>(nameof(IsSelected));

	public string? Text
	{
		get => GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	[Content]
	public IEnumerable<string>? Items
	{
		get => _items;
		set => SetAndRaise(ItemsProperty, ref _items, value);
	}

	public bool IsCurrentTextValid
	{
		get => GetValue(IsCurrentTextValidProperty);
		private set => SetValue(IsCurrentTextValidProperty, value);
	}

	public bool RequestAdd
	{
		get => _requestAdd;
		set => SetAndRaise(RequestAddProperty, ref _requestAdd, value);
	}

	public bool ForceAdd
	{
		get => GetValue(ForceAddProperty);
		set => SetValue(ForceAddProperty, value);
	}

	public bool RestrictInputToSuggestions
	{
		get => GetValue(RestrictInputToSuggestionsProperty);
		set => SetValue(RestrictInputToSuggestionsProperty, value);
	}

	public int ItemCountLimit
	{
		get => GetValue(ItemCountLimitProperty);
		set => SetValue(ItemCountLimitProperty, value);
	}

	public char TagSeparator
	{
		get => GetValue(TagSeparatorProperty);
		set => SetValue(TagSeparatorProperty, value);
	}

	public IList<string>? Suggestions
	{
		get => _suggestions;
		set => SetAndRaise(SuggestionsProperty, ref _suggestions, value);
	}

	public bool IsReadOnly
	{
		get => GetValue(IsReadOnlyProperty);
		set => SetValue(IsReadOnlyProperty, value);
	}

	public bool SuggestionsAreCaseSensitive
	{
		get => GetValue(SuggestionsAreCaseSensitiveProperty);
		set => SetValue(SuggestionsAreCaseSensitiveProperty, value);
	}

	public bool AllowDuplication
	{
		get => GetValue(AllowDuplicationProperty);
		set => SetValue(AllowDuplicationProperty, value);
	}

	public bool EnableCounter
	{
		get => GetValue(EnableCounterProperty);
		set => SetValue(EnableCounterProperty, value);
	}

	public bool EnableDelete
	{
		get => GetValue(EnableDeleteProperty);
		set => SetValue(EnableDeleteProperty, value);
	}

	public int MaxTextLength
	{
		get => GetValue(MaxTextLengthProperty);
		set => SetValue(MaxTextLengthProperty, value);
	}

	public bool IsSelected
	{
		get => GetValue(IsSelectedProperty);
		set => SetValue(IsSelectedProperty, value);
	}

	private string CurrentText => _autoCompleteBox?.Text ?? "";

	protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
	{
		base.OnApplyTemplate(e);

		_autoCompleteBox = e.NameScope.Find<TagsBoxAutoCompleteBox>("PART_AutoCompleteBox");

		if (_autoCompleteBox is not null)
		{
			_autoCompleteBox.Loaded += PresenterOnLoaded;
		}
	}

	private void PresenterOnLoaded(object? sender, RoutedEventArgs e)
	{
		Initialize();
	}

	private void Initialize()
	{
		_compositeDisposable?.Dispose();
		_compositeDisposable = new CompositeDisposable();


		if (_autoCompleteBox is null)
		{
			return;
		}

		_autoCompleteBox.InternalTextBox.WhenAnyValue(x => x.IsFocused)
					.Where(isFocused => isFocused == false)
					.Subscribe(_ => RequestAdd = true)
					.DisposeWith(_compositeDisposable);

		Observable
			.FromEventPattern(_autoCompleteBox.SuggestionListBox, nameof(PointerReleased))
			.Subscribe(_ => RequestAdd = true)
			.DisposeWith(_compositeDisposable);

		Observable
			.FromEventPattern<CancelEventArgs>(_autoCompleteBox, nameof(_autoCompleteBox.DropDownOpening))
			.Select(x => (AutoCompleteBox: (x.Sender as AutoCompleteBox)!, EventArgs: x.EventArgs))
			.Where(x => string.IsNullOrEmpty(x.AutoCompleteBox.Text))
			.Subscribe(x => x.EventArgs.Cancel = true)
			.DisposeWith(_compositeDisposable);

		_autoCompleteBox
			.WhenAnyValue(x => x.Text)
			.Where(string.IsNullOrEmpty)
			.Subscribe(_ => _autoCompleteBox.IsDropDownOpen = false)
			.DisposeWith(_compositeDisposable);

		_autoCompleteBox
			.AddDisposableHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel)
			.DisposeWith(_compositeDisposable);

		_autoCompleteBox
			.AddDisposableHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel)
			.DisposeWith(_compositeDisposable);

		Observable.Merge(
				this.WhenAnyValue(x => x.RequestAdd).Where(x => x).Throttle(TimeSpan.FromMilliseconds(10)).ToSignal(),
				this.WhenAnyValue(x => x.ForceAdd).Where(x => x).ToSignal())
			.ObserveOn(RxApp.MainThreadScheduler)
			.Select(_ => CurrentText)
			.Subscribe(currentText =>
			{
				Dispatcher.UIThread.Post(() =>
				{
					RequestAdd = false;
					ForceAdd = false;
				});
				ClearInputField();
				var tag = GetFinalTag(currentText);
				if (tag is not null)
				{
					AddTag(tag);
				}
			})
			.DisposeWith(_compositeDisposable);

		_autoCompleteBox.WhenAnyValue(x => x.Text)
			.Subscribe(_ =>
			{
				CheckIsCurrentTextValid();
			})
			.DisposeWith(_compositeDisposable);

		this.WhenAnyValue(x => x.Items)
			.Subscribe(_ =>
			{
				CheckIsCurrentTextValid();
			})
			.DisposeWith(_compositeDisposable);
	}

	private void CheckIsCurrentTextValid()
	{
		var correctedInput = CurrentText.ParseLabel();

		if (RestrictInputToSuggestions && Suggestions is { } suggestions)
		{
			IsCurrentTextValid = suggestions.Any(x => x.Equals(correctedInput, _stringComparison));
			return;
		}

		if (!RestrictInputToSuggestions)
		{
			IsCurrentTextValid = !string.IsNullOrEmpty(correctedInput);
			return;
		}

		IsCurrentTextValid = false;
	}

	private void OnKeyDown(object? sender, KeyEventArgs e)
	{
		if (!_isInputEnabled && e.Key != Key.Back)
		{
			return;
		}

		var emptyInputField = string.IsNullOrEmpty(CurrentText);

		switch (e.Key)
		{
			case Key.Back when emptyInputField:
				RemoveLastTag();
				break;

			case Key.Enter or Key.Tab when !emptyInputField:
				RequestAdd = true;
				e.Handled = true;
				break;
		}
	}

	private void ClearInputField()
	{
		_autoCompleteBox?.ClearValue(AutoCompleteBox.SelectedItemProperty);
		Dispatcher.UIThread.Post(() => _autoCompleteBox?.ClearValue(AutoCompleteBox.TextProperty));
	}

	private string? GetFinalTag(string input)
	{
		if (!string.IsNullOrEmpty(input))
		{
			return input.ParseLabel();
		}

		return null;
	}

	protected override void OnGotFocus(GotFocusEventArgs e)
	{
		base.OnGotFocus(e);

		_autoCompleteBox?.InternalTextBox?.Focus();
	}

	private void CheckIsInputEnabled()
	{
		if (Items is IList items && ItemCountLimit > 0)
		{
			_isInputEnabled = items.Count < ItemCountLimit;
		}
	}

	private void OnTextInput(object? sender, TextInputEventArgs e)
	{
		if (sender is not AutoCompleteBox autoCompleteBox)
		{
			return;
		}

		var typedFullText = autoCompleteBox.SearchText + e.Text;

		if (!_isInputEnabled ||
		    (typedFullText is { Length: 1 } && typedFullText.StartsWith(TagSeparator)) ||
		    string.IsNullOrEmpty(typedFullText.ParseLabel()))
		{
			e.Handled = true;
			return;
		}

		var suggestions = Suggestions?.ToArray();

		if (RestrictInputToSuggestions &&
		    suggestions is { } &&
		    !suggestions.Any(x => x.StartsWith(typedFullText, _stringComparison)))
		{
			if (!typedFullText.EndsWith(TagSeparator) ||
			    (typedFullText.EndsWith(TagSeparator) && !suggestions.Contains(autoCompleteBox.SearchText)))
			{
				e.Handled = true;
				return;
			}
		}

		if (e.Text is { Length: 1 } && e.Text.StartsWith(TagSeparator))
		{
			autoCompleteBox.Text = autoCompleteBox.SearchText;
			RequestAdd = true;
			e.Handled = true;
		}
	}

	protected override void UpdateDataValidation(AvaloniaProperty property, BindingValueType state, Exception error)
	{
		if (property == ItemsProperty)
		{
			DataValidationErrors.SetError(this, error);
		}
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
	{
		base.OnPropertyChanged(e);

		if (e.Property == IsReadOnlyProperty)
		{
			PseudoClasses.Set(":readonly", IsReadOnly);
		}
		else if (e.Property == SuggestionsAreCaseSensitiveProperty)
		{
			_stringComparison = SuggestionsAreCaseSensitive
				? StringComparison.CurrentCulture
				: StringComparison.CurrentCultureIgnoreCase;
		}
	}

	private void RemoveLastTag()
	{
		if (Items is IList { Count: > 0 } items)
		{
			RemoveAt(items.Count - 1);
		}
	}

	public void RemoveAt(int index)
	{
		if (Items is not IList items)
		{
			return;
		}

		items.RemoveAt(index);
		CheckIsInputEnabled();
	}

	public void AddTag(object? value)
	{
		if (value is string tag)
		{
			AddTag(tag);
		}
	}

	public void AddTag(string tag)
	{
		var inputTag = tag;

		/*
		if (Items is not IList items)
		{
			return;
		}

		if (ItemCountLimit > 0 && items.Count + 1 > ItemCountLimit)
		{
			return;
		}

		if (!AllowDuplication && items.Contains(tag))
		{
			return;
		}
		*/

		if (Suggestions is { } suggestions)
		{
			if (RestrictInputToSuggestions &&
			    !suggestions.Any(x => x.Equals(tag, _stringComparison)))
			{
				return;
			}

			// When user tries to commit a tag,
			// check if it's already in the suggestions list
			// by comparing it case-insensitively.
			var result = suggestions.FirstOrDefault(x => x.Equals(tag, StringComparison.CurrentCultureIgnoreCase));

			if (result is not null)
			{
				inputTag = result;
			}
		}

		//items.Add(inputTag);

		SetCurrentValue(TextProperty, tag);

		CheckIsInputEnabled();
	}
}

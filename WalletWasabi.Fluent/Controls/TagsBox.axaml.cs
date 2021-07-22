using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Metadata;
using Avalonia.Threading;
using ReactiveUI;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Helpers;

namespace WalletWasabi.Fluent.Controls
{
	public class TagsBox : TemplatedControl
	{
		public static readonly StyledProperty<string> WatermarkProperty =
			TextBox.WatermarkProperty.AddOwner<TagsBox>();

		public static readonly StyledProperty<bool> RestrictInputToSuggestionsProperty =
			AvaloniaProperty.Register<TagsBox, bool>(nameof(RestrictInputToSuggestions));

		public static readonly StyledProperty<int> ItemCountLimitProperty =
			AvaloniaProperty.Register<TagsBox, int>(nameof(ItemCountLimit));

		public static readonly StyledProperty<char> TagSeparatorProperty =
			AvaloniaProperty.Register<TagsBox, char>(nameof(TagSeparator), defaultValue: ' ');

		public static readonly StyledProperty<bool> SuggestionsAreCaseSensitiveProperty =
			AvaloniaProperty.Register<TagsBox, bool>(nameof(SuggestionsAreCaseSensitive), defaultValue: true);

		public static readonly StyledProperty<bool> AllowDuplicationProperty =
			AvaloniaProperty.Register<TagsBox, bool>(nameof(AllowDuplication));

		public static readonly DirectProperty<TagsBox, IEnumerable<string>?> ItemsProperty =
			AvaloniaProperty.RegisterDirect<TagsBox, IEnumerable<string>?>(nameof(Items),
				o => o.Items,
				(o, v) => o.Items = v,
				enableDataValidation: true);

		public static readonly DirectProperty<TagsBox, IEnumerable?> SuggestionsProperty =
			AvaloniaProperty.RegisterDirect<TagsBox, IEnumerable?>(
				nameof(Suggestions),
				o => o.Suggestions,
				(o, v) => o.Suggestions = v);

		private CompositeDisposable? _compositeDisposable;
		private AutoCompleteBox? _autoCompleteBox;
		private TextBox? _internalTextBox;
		private TextBlock? _watermark;
		private StringComparison _stringComparison;
		private bool _backspaceEmptyField1;
		private bool _backspaceEmptyField2;
		private bool _isInputEnabled = true;
		private IEnumerable? _suggestions;
		private ICommand? _completedCommand;
		private IEnumerable<string>? _items;

		public static readonly DirectProperty<TagsBox, ICommand?> CompletedCommandProperty =
			AvaloniaProperty.RegisterDirect<TagsBox, ICommand?>(
				nameof(CompletedCommand),
				o => o.CompletedCommand,
				(o, v) => o.CompletedCommand = v);

		public static readonly StyledProperty<bool> IsReadOnlyProperty =
			AvaloniaProperty.Register<TagsBox, bool>("IsReadOnly");

		[Content]
		public IEnumerable<string>? Items
		{
			get => _items;
			set => SetAndRaise(ItemsProperty, ref _items, value);
		}

		public string Watermark
		{
			get => GetValue(WatermarkProperty);
			set => SetValue(WatermarkProperty, value);
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

		public IEnumerable? Suggestions
		{
			get => _suggestions;
			set => SetAndRaise(SuggestionsProperty, ref _suggestions, value);
		}

		public ICommand? CompletedCommand
		{
			get => _completedCommand;
			set => SetAndRaise(CompletedCommandProperty, ref _completedCommand, value);
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

		protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
		{
			base.OnApplyTemplate(e);

			_compositeDisposable?.Dispose();

			_compositeDisposable = new CompositeDisposable();

			var presenter = e.NameScope.Find<ItemsPresenter>("PART_ItemsPresenter");

			presenter.ApplyTemplate();

			_watermark = e.NameScope.Find<TextBlock>("PART_Watermark");

			_autoCompleteBox = (presenter.Panel as ConcatenatingWrapPanel)?.ConcatenatedChildren
				.OfType<AutoCompleteBox>().FirstOrDefault();

			if (_autoCompleteBox is null)
			{
				return;
			}

			_autoCompleteBox.TextChanged += OnAutoCompleteBoxTextChanged;
			_autoCompleteBox.DropDownClosed += OnAutoCompleteBoxDropDownClosed;
			_autoCompleteBox.TemplateApplied += OnAutoCompleteBoxTemplateApplied;

			_autoCompleteBox.FilterMode = AutoCompleteFilterMode.StartsWith;

			Disposable.Create(
					() =>
					{
						_autoCompleteBox.TextChanged -= OnAutoCompleteBoxTextChanged;
						_autoCompleteBox.DropDownClosed -= OnAutoCompleteBoxDropDownClosed;
						_autoCompleteBox.TemplateApplied -= OnAutoCompleteBoxTemplateApplied;
					})
				.DisposeWith(_compositeDisposable);

			_autoCompleteBox
				.AddDisposableHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel)
				.DisposeWith(_compositeDisposable);

			_autoCompleteBox
				.AddDisposableHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel)
				.DisposeWith(_compositeDisposable);
		}

		private void OnAutoCompleteBoxTemplateApplied(object? sender, TemplateAppliedEventArgs e)
		{
			_internalTextBox = e.NameScope.Find<TextBox>("PART_TextBox");
			_internalTextBox.WhenAnyValue(x => x.IsFocused)
				.Subscribe(isFocused =>
				{
					if (isFocused || !_isInputEnabled || string.IsNullOrWhiteSpace(_internalTextBox.Text) || _autoCompleteBox is { IsDropDownOpen: true })
					{
						return;
					}

					var currentText = (_autoCompleteBox?.Text ?? "").Trim();

					if (RestrictInputToSuggestions &&
						Suggestions is IList<string> suggestions &&
						!suggestions.Any(x =>
							x.StartsWith(currentText, _stringComparison)))
					{
						return;
					}

					AddTag(currentText);
					BackspaceLogicClear();
					_autoCompleteBox?.ClearValue(AutoCompleteBox.SelectedItemProperty);
					Dispatcher.UIThread.Post(() => _autoCompleteBox?.ClearValue(AutoCompleteBox.TextProperty));
				})
				.DisposeWith(_compositeDisposable!);
		}

		protected override void OnGotFocus(GotFocusEventArgs e)
		{
			base.OnGotFocus(e);

			_internalTextBox?.Focus();
		}

		private void CheckIsInputEnabled()
		{
			if (Items is IList x && ItemCountLimit > 0)
			{
				_isInputEnabled = x.Count < ItemCountLimit;
			}
		}

		private void InvalidateWatermark()
		{
			if (_watermark is { } && _autoCompleteBox is { })
			{
				if ((Items is null || (Items is { } && !Items.Any())) && string.IsNullOrEmpty(_autoCompleteBox?.Text))
				{
					_watermark.IsVisible = true;
				}
				else
				{
					_watermark.IsVisible = false;
				}
			}
		}

		private void OnTextInput(object? sender, TextInputEventArgs e)
		{
			if (sender is not AutoCompleteBox autoCompleteBox)
			{
				return;
			}

			if (!_isInputEnabled)
			{
				e.Handled = true;
				return;
			}

			InvalidateWatermark();

			if (RestrictInputToSuggestions &&
				Suggestions is IList<string> suggestions &&
				!suggestions.Any(x =>
					x.StartsWith(autoCompleteBox.SearchText, _stringComparison)))
			{
				e.Handled = true;
			}
		}

		protected override void UpdateDataValidation<T>(AvaloniaProperty<T> property, BindingValue<T> value)
		{
			if (property == ItemsProperty)
			{
				DataValidationErrors.SetError(this, value.Error);
			}
		}

		private void OnAutoCompleteBoxDropDownClosed(object? sender, EventArgs e)
		{
			if (sender is not AutoCompleteBox autoCompleteBox)
			{
				return;
			}

			if (_internalTextBox is null)
			{
				return;
			}

			var currentText = (autoCompleteBox.Text ?? "").Trim();

			if (currentText.Length == 0 ||
				autoCompleteBox.SelectedItem is not string selItem ||
				selItem.Length == 0 ||
				currentText != selItem)
			{
				return;
			}

			AddTag(currentText);
			BackspaceLogicClear();
			autoCompleteBox.ClearValue(AutoCompleteBox.SelectedItemProperty);
			Dispatcher.UIThread.Post(() => autoCompleteBox.ClearValue(AutoCompleteBox.TextProperty));
		}

		private void BackspaceLogicClear()
		{
			_backspaceEmptyField2 = _backspaceEmptyField1 = true;
		}

		private void OnAutoCompleteBoxTextChanged(object? sender, EventArgs e)
		{
			InvalidateWatermark();

			if (sender is not AutoCompleteBox autoCompleteBox ||
				string.IsNullOrEmpty(Guard.Correct(autoCompleteBox.Text)))
			{
				return;
			}

			var currentText = autoCompleteBox.Text ?? "";
			var endsWithSeparator = currentText.EndsWith(TagSeparator);
			currentText = currentText.Trim();

			var splitTags = currentText.Split(TagSeparator);

			if (splitTags.Length <= 1)
			{
				var tag = splitTags[0];

				if (!_isInputEnabled ||
					!endsWithSeparator)
				{
					return;
				}

				if (RestrictInputToSuggestions && Suggestions is { } &&
					!Suggestions.Cast<string>().Any(
						x => x.Equals(tag, _stringComparison)))
				{
					return;
				}

				AddTag(tag);
				BackspaceLogicClear();
				Dispatcher.UIThread.Post(() => autoCompleteBox.ClearValue(AutoCompleteBox.TextProperty));
			}
			else
			{
				foreach (var tag in splitTags)
				{
					if (string.IsNullOrWhiteSpace(tag))
					{
						continue;
					}

					if (RestrictInputToSuggestions && Suggestions is { } &&
						!Suggestions.Cast<string>().Any(
							x => x.Equals(tag, _stringComparison)))
					{
						continue;
					}

					AddTag(tag);
					BackspaceLogicClear();
					Dispatcher.UIThread.Post(() => autoCompleteBox.ClearValue(AutoCompleteBox.TextProperty));
				}
			}
		}

		private void OnKeyDown(object? sender, KeyEventArgs e)
		{
			if (sender is not AutoCompleteBox autoCompleteBox)
			{
				return;
			}

			var currentText = autoCompleteBox.Text ?? "";

			_backspaceEmptyField2 = _backspaceEmptyField1;
			_backspaceEmptyField1 = currentText.Length == 0;
			var selectedTextLength = Math.Max(0, _internalTextBox!.SelectionEnd - _internalTextBox.SelectionStart);

			currentText = currentText.Trim();

			switch (e.Key)
			{
				case Key.Back when _backspaceEmptyField1 && _backspaceEmptyField2:
					RemoveLastTag();
					break;

				case Key.Tab when _isInputEnabled && !string.IsNullOrEmpty(currentText) && selectedTextLength == 0:
				case Key.Enter when _isInputEnabled && !string.IsNullOrEmpty(currentText) && selectedTextLength == 0:
					// Reject entry of the tag when user pressed enter and
					// the input tag is not on the suggestions list.
					if (RestrictInputToSuggestions && Suggestions is { } &&
						!Suggestions.Cast<string>().Any(
							x => x.Equals(currentText, _stringComparison)))
					{
						break;
					}

					BackspaceLogicClear();
					AddTag(currentText);
					ExecuteCompletedCommand();

					Dispatcher.UIThread.Post(() => autoCompleteBox.ClearValue(AutoCompleteBox.TextProperty));
					e.Handled = true;

					break;

				case Key.Enter:
					ExecuteCompletedCommand();
					break;
			}
		}

		private void ExecuteCompletedCommand()
		{
			if (Items is IList x && x.Count >= ItemCountLimit)
			{
				if (CompletedCommand is { } && CompletedCommand.CanExecute(null))
				{
					CompletedCommand.Execute(null);
				}
			}
		}

		private void RemoveLastTag()
		{
			if (Items is IList { Count: > 0 } list)
			{
				list.RemoveAt(list.Count - 1);
			}

			InvalidateWatermark();
			CheckIsInputEnabled();
		}

		protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> e)
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

		internal void RemoveTargetTag(object? tag)
		{
			if (Items is IList list)
			{
				list.Remove(tag);
			}

			InvalidateWatermark();
			CheckIsInputEnabled();
		}

		public void AddTag(string tag)
		{
			if (Items is IList x)
			{
				if (ItemCountLimit > 0 && x.Count + 1 > ItemCountLimit)
				{
					return;
				}

				var finalTag = tag.ParseLabel();

				if (!AllowDuplication && x.Contains(finalTag))
				{
					return;
				}

				x.Add(finalTag);
			}

			InvalidateWatermark();
			CheckIsInputEnabled();
		}
	}
}

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Infrastructure;
using WalletWasabi.Helpers;

namespace WalletWasabi.Fluent.Controls;

public partial class CurrencyEntryBox : TextBox
{
	public static readonly StyledProperty<string> CurrencyCodeProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, string>(nameof(CurrencyCode));

	public static readonly StyledProperty<bool> IsFiatProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, bool>(nameof(IsFiat));

	public static readonly StyledProperty<bool> IsApproximateProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, bool>(nameof(IsApproximate));

	public static readonly StyledProperty<decimal> ConversionRateProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, decimal>(nameof(ConversionRate));

	public static readonly StyledProperty<bool> IsRightSideProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, bool>(nameof(IsRightSide));

	public static readonly StyledProperty<int> MaxDecimalsProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, int>(nameof(MaxDecimals), 8);

	public static readonly StyledProperty<Money> BalanceBtcProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, Money>(nameof(BalanceBtc));

	public static readonly StyledProperty<decimal> BalanceUsdProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, decimal>(nameof(BalanceUsd));

	public static readonly StyledProperty<bool> ValidatePasteBalanceProperty =
		AvaloniaProperty.Register<CurrencyEntryBox, bool>(nameof(ValidatePasteBalance));

	public CurrencyEntryBox()
	{
		Text = string.Empty;

		PseudoClasses.Set(":noexchangerate", true);
		PseudoClasses.Set(":isrightside", false);

		this.GetObservable(IsRightSideProperty)
			.Subscribe(x => PseudoClasses.Set(":isrightside", x));

		ModifiedPaste = ReactiveCommand.Create(ModifiedPasteAsync, this.GetObservable(CanPasteProperty));
	}

	public ICommand ModifiedPaste { get; }

	public decimal ConversionRate
	{
		get => GetValue(ConversionRateProperty);
		set => SetValue(ConversionRateProperty, value);
	}

	public string CurrencyCode
	{
		get => GetValue(CurrencyCodeProperty);
		set => SetValue(CurrencyCodeProperty, value);
	}

	public bool IsFiat
	{
		get => GetValue(IsFiatProperty);
		set => SetValue(IsFiatProperty, value);
	}

	public bool IsApproximate
	{
		get => GetValue(IsApproximateProperty);
		set => SetValue(IsApproximateProperty, value);
	}

	public bool IsRightSide
	{
		get => GetValue(IsRightSideProperty);
		set => SetValue(IsRightSideProperty, value);
	}

	public int MaxDecimals
	{
		get => GetValue(MaxDecimalsProperty);
		set => SetValue(MaxDecimalsProperty, value);
	}

	public Money BalanceBtc
	{
		get => GetValue(BalanceBtcProperty);
		set => SetValue(BalanceBtcProperty, value);
	}

	public decimal BalanceUsd
	{
		get => GetValue(BalanceUsdProperty);
		set => SetValue(BalanceUsdProperty, value);
	}

	public bool ValidatePasteBalance
	{
		get => GetValue(ValidatePasteBalanceProperty);
		set => SetValue(ValidatePasteBalanceProperty, value);
	}

	private decimal FiatToBitcoin(decimal fiatValue)
	{
		if (ConversionRate == 0m)
		{
			return 0m;
		}

		return fiatValue / ConversionRate;
	}

	protected override void OnGotFocus(GotFocusEventArgs e)
	{
		base.OnGotFocus(e);

		CaretIndex = Text?.Length ?? 0;

		Dispatcher.UIThread.Post(SelectAll);
	}

	protected override void OnTextInput(TextInputEventArgs e)
	{
		var input = e.Text == null ? "" : e.Text.TotalTrim();

		// Reject space char input when there's no text.
		if (string.IsNullOrWhiteSpace(Text) && string.IsNullOrWhiteSpace(input))
		{
			e.Handled = true;
			base.OnTextInput(e);
			return;
		}

		if (IsReplacingWithImplicitDecimal(input))
		{
			ReplaceCurrentTextWithLeadingZero(e);

			base.OnTextInput(e);
			return;
		}

		if (IsInsertingImplicitDecimal(input))
		{
			InsertLeadingZeroForDecimal(e);

			base.OnTextInput(e);
			return;
		}

		var preComposedText = PreComposeText(input);

		var isValid = ValidateEntryText(preComposedText, validatePasteBalance: false);

		e.Handled = !isValid;

		base.OnTextInput(e);
	}

	private bool IsReplacingWithImplicitDecimal(string input)
	{
		return input.StartsWith(".") && SelectedText == Text;
	}

	private bool IsInsertingImplicitDecimal(string input)
	{
		return input.StartsWith(".") && CaretIndex == 0 && Text is not null && !Text.Contains('.');
	}

	private void ReplaceCurrentTextWithLeadingZero(TextInputEventArgs e)
	{
		var finalText = "0" + e.Text;
		Text = "";
		e.Text = finalText;
		CaretIndex = finalText.Length;
		ClearSelection();
	}

	private void InsertLeadingZeroForDecimal(TextInputEventArgs e)
	{
		var prependText = "0" + e.Text;
		Text = Text.Insert(0, prependText);
		e.Text = "";
		CaretIndex += prependText.Length;
	}

	private bool ValidateEntryText(string preComposedText, bool validatePasteBalance)
	{
		if (!TryParse(preComposedText, validatePasteBalance, out _))
		{
			return false;
		}

		return true;
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		DoPasteCheck(e);
	}

	private void DoPasteCheck(KeyEventArgs e)
	{
		var keymap = AvaloniaLocator.Current.GetService<PlatformHotkeyConfiguration>();

		bool Match(IEnumerable<KeyGesture> gestures) => gestures.Any(g => g.Matches(e));

		if (keymap is { } && Match(keymap.Paste))
		{
			ModifiedPasteAsync();
		}
		else
		{
			base.OnKeyDown(e);
		}
	}

	public async void ModifiedPasteAsync()
	{
		if (AvaloniaLocator.Current.GetService<IClipboard>() is { } clipboard)
		{
			var text = await clipboard.GetTextAsync();

			if (string.IsNullOrEmpty(text))
			{
				return;
			}

			text = text.Replace("\r", "").Replace("\n", "").Trim();

			if (!TryParse(text, ValidatePasteBalance, out text))
			{
				return;
			}

			if (ValidateEntryText(text, ValidatePasteBalance))
			{
				OnTextInput(new TextInputEventArgs { Text = text });
			}
		}
	}

	private bool TryParse(string text, bool validatePasteBalance, [NotNullWhen(true)] out string? result)
	{
		var money = validatePasteBalance
			? ClipboardObserver.ParseToMoney(text, BalanceBtc)
			: ClipboardObserver.ParseToMoney(text);
		if (money is not null)
		{
			result = money.ToDecimal(MoneyUnit.BTC).FormattedBtc();
			return true;
		}

		var usd = validatePasteBalance
			? ClipboardObserver.ParseToUsd(text, BalanceUsd)
			: ClipboardObserver.ParseToUsd(text);
		if (usd is not null)
		{
			result = usd.Value.ToString("0.00");
			return true;
		}

		result = null;
		return false;
	}

	// Pre-composes the TextInputEventArgs to see the potential Text that is to
	// be committed to the TextPresenter in this control.

	// An event in Avalonia's TextBox with this function should be implemented there for brevity.
	private string PreComposeText(string input)
	{
		input = RemoveInvalidCharacters(input);
		var preComposedText = Text ?? "";
		var caretIndex = CaretIndex;
		var selectionStart = SelectionStart;
		var selectionEnd = SelectionEnd;

		if (!string.IsNullOrEmpty(input) && (MaxLength == 0 ||
											 input.Length + preComposedText.Length -
											 Math.Abs(selectionStart - selectionEnd) <= MaxLength))
		{
			if (selectionStart != selectionEnd)
			{
				var start = Math.Min(selectionStart, selectionEnd);
				var end = Math.Max(selectionStart, selectionEnd);
				preComposedText = $"{preComposedText[..start]}{preComposedText[end..]}";
				caretIndex = start;
			}

			return $"{preComposedText[..caretIndex]}{input}{preComposedText[caretIndex..]}";
		}

		return "";
	}

	protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change)
	{
		base.OnPropertyChanged(change);

		if (change.Property == IsReadOnlyProperty)
		{
			PseudoClasses.Set(":readonly", change.NewValue.GetValueOrDefault<bool>());
		}
		else if (change.Property == ConversionRateProperty)
		{
			PseudoClasses.Set(":noexchangerate", change.NewValue.GetValueOrDefault<decimal>() == 0m);
		}
		else if (change.Property == IsFiatProperty)
		{
			PseudoClasses.Set(":isfiat", change.NewValue.GetValueOrDefault<bool>());
		}
	}
}

using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Userfacing;

namespace WalletWasabi.Fluent.Controls;

public class DualCurrencyEntryBox : TemplatedControl
{
	public static readonly StyledProperty<HorizontalAlignment> HorizontalContentAlignmentProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, HorizontalAlignment>(nameof(HorizontalContentAlignment));

	public static readonly StyledProperty<VerticalAlignment> VerticalContentAlignmentProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, VerticalAlignment>(nameof(VerticalContentAlignment));

	public static readonly DirectProperty<DualCurrencyEntryBox, decimal?> AmountBtcProperty =
		AvaloniaProperty.RegisterDirect<DualCurrencyEntryBox, decimal?>(
			nameof(AmountBtc),
			o => o.AmountBtc,
			(o, v) => o.AmountBtc = v,
			enableDataValidation: true,
			defaultBindingMode: BindingMode.TwoWay);

	public static readonly StyledProperty<string?> TextProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, string?>(nameof(Text));

	public static readonly StyledProperty<string> WatermarkProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, string>(nameof(Watermark));

	public static readonly StyledProperty<string?> ConversionTextProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, string?>(nameof(ConversionText));

	public static readonly StyledProperty<decimal> ConversionRateProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, decimal>(nameof(ConversionRate));

	public static readonly StyledProperty<string> CurrencyCodeProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, string>(nameof(CurrencyCode));

	public static readonly StyledProperty<string> ConversionCurrencyCodeProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, string>(nameof(ConversionCurrencyCode));

	public static readonly StyledProperty<string> ConversionWatermarkProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, string>(nameof(ConversionWatermark));

	public static readonly StyledProperty<bool> IsConversionReversedProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, bool>(nameof(IsConversionReversed));

	public static readonly StyledProperty<bool> IsConversionApproximateProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, bool>(nameof(IsConversionApproximate));

	public static readonly StyledProperty<bool> IsReadOnlyProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, bool>(nameof(IsReadOnly));

	public static readonly StyledProperty<int> LeftColumnProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, int>(nameof(LeftColumn));

	public static readonly StyledProperty<int> RightColumnProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, int>(nameof(RightColumn));

	public static readonly StyledProperty<CurrencyEntryBox?> RightEntryBoxProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, CurrencyEntryBox?>(nameof(RightEntryBox));

	public static readonly StyledProperty<CurrencyEntryBox?> LeftEntryBoxProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, CurrencyEntryBox?>(nameof(LeftEntryBox));

	public static readonly StyledProperty<Money> BalanceBtcProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, Money>(nameof(BalanceBtc));

	public static readonly StyledProperty<decimal> BalanceUsdProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, decimal>(nameof(BalanceUsd));

	public static readonly StyledProperty<bool> ValidatePasteBalanceProperty =
		AvaloniaProperty.Register<DualCurrencyEntryBox, bool>(nameof(ValidatePasteBalance));

	private CompositeDisposable? _disposable;
	private Button? _swapButton;
	private decimal? _amountBtc;
	private bool _isTextInputFocused;
	private bool _isConversationTextFocused;
	private bool _skipProcessing;
	private bool _skipTextProcessing;

	public DualCurrencyEntryBox()
	{
		this.GetObservable(TextProperty).Where(_ => !_skipTextProcessing).Subscribe(InputText);
		this.GetObservable(ConversionTextProperty).Where(_ => !_skipTextProcessing).Subscribe(InputConversionText);
		this.GetObservable(ConversionRateProperty).Subscribe(_ => UpdateDisplay());
		this.GetObservable(ConversionCurrencyCodeProperty).Subscribe(_ => UpdateDisplay());
		this.GetObservable(IsReadOnlyProperty).Subscribe(_ => UpdateDisplay());
		this.GetObservable(AmountBtcProperty).Where(_ => !_skipProcessing).Subscribe(_ => UpdateDisplay(true));

		UpdateDisplay();

		PseudoClasses.Set(":noexchangerate", true);

		FocusCommand = ReactiveCommand.Create(FocusOnLeftEntryBox);
	}

	public HorizontalAlignment HorizontalContentAlignment
	{
		get { return GetValue(HorizontalContentAlignmentProperty); }
		set { SetValue(HorizontalContentAlignmentProperty, value); }
	}

	public VerticalAlignment VerticalContentAlignment
	{
		get { return GetValue(VerticalContentAlignmentProperty); }
		set { SetValue(VerticalContentAlignmentProperty, value); }
	}

	public decimal? AmountBtc
	{
		get => _amountBtc;
		set => SetAndRaise(AmountBtcProperty, ref _amountBtc, value);
	}

	public string? Text
	{
		get => GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	public string Watermark
	{
		get => GetValue(WatermarkProperty);
		set => SetValue(WatermarkProperty, value);
	}

	public string? ConversionText
	{
		get => GetValue(ConversionTextProperty);
		set => SetValue(ConversionTextProperty, value);
	}

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

	public string ConversionCurrencyCode
	{
		get => GetValue(ConversionCurrencyCodeProperty);
		set => SetValue(ConversionCurrencyCodeProperty, value);
	}

	public string ConversionWatermark
	{
		get => GetValue(ConversionWatermarkProperty);
		set => SetValue(ConversionWatermarkProperty, value);
	}

	public bool IsConversionApproximate
	{
		get => GetValue(IsConversionApproximateProperty);
		set => SetValue(IsConversionApproximateProperty, value);
	}

	public bool IsConversionReversed
	{
		get => GetValue(IsConversionReversedProperty);
		set => SetValue(IsConversionReversedProperty, value);
	}

	public bool IsReadOnly
	{
		get => GetValue(IsReadOnlyProperty);
		set => SetValue(IsReadOnlyProperty, value);
	}

	public int LeftColumn
	{
		get => GetValue(LeftColumnProperty);
		set => SetValue(LeftColumnProperty, value);
	}

	public int RightColumn
	{
		get => GetValue(RightColumnProperty);
		set => SetValue(RightColumnProperty, value);
	}

	public CurrencyEntryBox? RightEntryBox
	{
		get => GetValue(RightEntryBoxProperty);
		set => SetValue(RightEntryBoxProperty, value);
	}

	public CurrencyEntryBox? LeftEntryBox
	{
		get => GetValue(LeftEntryBoxProperty);
		set => SetValue(LeftEntryBoxProperty, value);
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

	public ICommand FocusCommand { get; }

	private void InputText(string? text)
	{
		if (!_isTextInputFocused)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(text))
		{
			SetBtcAmount(null);
		}
		else
		{
			if (CurrencyInput.TryCorrectBitcoinAmount(text, out var better))
			{
				text = better;
			}

			if (decimal.TryParse(text, NumberStyles.Number, CurrencyInput.InvariantNumberFormat, out var decimalValue))
			{
				SetBtcAmount(decimalValue);
			}
		}

		UpdateDisplay();
	}

	private void InputConversionText(string? text)
	{
		if (!_isConversationTextFocused)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(text))
		{
			SetBtcAmount(null);
		}
		else
		{
			if (decimal.TryParse(text, NumberStyles.Number, CurrencyInput.InvariantNumberFormat, out var decimalValue))
			{
				SetBtcAmount(FiatToBitcoin(decimalValue));
			}
		}

		UpdateDisplay();
	}

	private void UpdateDisplay(bool insertValue = false)
	{
		_skipTextProcessing = true;
		UpdateTextDisplay(insertValue);
		UpdateConversationTextDisplay(insertValue);
		_skipTextProcessing = false;
	}

	private void UpdateTextDisplay(bool insertValue)
	{
		Watermark = FullFormatBtc(0);

		var text = LeftEntryBox?.Text ?? "";
		if (_isTextInputFocused)
		{
			text = insertValue ? AmountBtc?.ToString(CultureInfo.InvariantCulture) : RemoveFormat(text);
		}
		else
		{
			text = AmountBtc > 0 ? AmountBtc?.FormattedBtcFixedFractional() : string.Empty;
		}

		SetCurrentValue(TextProperty, text);
	}

	private void UpdateConversationTextDisplay(bool insertValue)
	{
		if (ConversionRate == 0m)
		{
			return;
		}

		SetCurrentValue(IsConversionApproximateProperty, AmountBtc > 0);
		SetCurrentValue(ConversionWatermarkProperty, FullFormatFiat(0, ConversionCurrencyCode, true));

		var conversion = BitcoinToFiat(AmountBtc);

		var text = RightEntryBox?.Text ?? "";
		if (_isConversationTextFocused)
		{
			text = insertValue ? RemoveFormat(conversion?.FormattedFiat() ?? "") : RemoveFormat(text);
		}
		else
		{
			text = AmountBtc > 0 ? conversion?.FormattedFiat() ?? string.Empty : string.Empty;
		}

		SetCurrentValue(ConversionTextProperty, text);
	}

	private void SetBtcAmount(decimal? amount)
	{
		_skipProcessing = true;
		SetCurrentValue(AmountBtcProperty, amount);
		_skipProcessing = false;
	}

	private decimal FiatToBitcoin(decimal fiatValue)
	{
		return fiatValue / ConversionRate;
	}

	private decimal? BitcoinToFiat(decimal? btcValue)
	{
		return btcValue * ConversionRate;
	}

	private static string FullFormatBtc(decimal value)
	{
		return $"BTC {value.FormattedBtc()}";
	}

	private static string FullFormatFiat(decimal value, string currencyCode, bool approximate)
	{
		var part1 = approximate ? "≈ " : "";
		var part2 =
			!string.IsNullOrWhiteSpace(currencyCode)
				? $"{currencyCode} "
				: "";
		var part3 = value.FormattedFiat();
		return part1 + part2 + part3;
	}

	protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
	{
		base.OnApplyTemplate(e);

		_disposable?.Dispose();
		_disposable = new CompositeDisposable();

		_swapButton = e.NameScope.Find<Button>("PART_SwapButton");
		LeftEntryBox = e.NameScope.Find<CurrencyEntryBox>("PART_LeftEntryBox");
		RightEntryBox = e.NameScope.Find<CurrencyEntryBox>("PART_RightEntryBox");

		LeftEntryBox?
			.GetObservable(IsKeyboardFocusWithinProperty)
			.Subscribe(x =>
			{
				if (LeftEntryBox.ContextFlyout is null || LeftEntryBox.ContextFlyout.IsOpen)
				{
					return;
				}

				_isTextInputFocused = x;
				UpdateDisplay();
			})
			.DisposeWith(_disposable);

		RightEntryBox?
			.GetObservable(IsKeyboardFocusWithinProperty)
			.Subscribe(x =>
			{
				if (RightEntryBox.ContextFlyout is null || RightEntryBox.ContextFlyout.IsOpen)
				{
					return;
				}

				_isConversationTextFocused = x;
				UpdateDisplay();
			})
			.DisposeWith(_disposable);

		if (_swapButton is { })
		{
			_swapButton.Click += SwapButtonOnClick;

			_disposable.Add(Disposable.Create(() => _swapButton.Click -= SwapButtonOnClick));
		}

		ReorganizeVisuals();
	}

	private void SwapButtonOnClick(object? sender, RoutedEventArgs e)
	{
		SetCurrentValue(IsConversionReversedProperty, !IsConversionReversed);
		FocusOnLeftEntryBox();
	}

	private void FocusOnLeftEntryBox()
	{
		var focusOn =
			IsConversionReversed
				? RightEntryBox
				: LeftEntryBox;

		focusOn?.Focus();
	}

	private string RemoveFormat(string text) => text.Replace(" ", "");

	protected override void UpdateDataValidation(AvaloniaProperty property, BindingValueType state, Exception? error)
	{
		if (property == AmountBtcProperty)
		{
			DataValidationErrors.SetError(this, error);
		}
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property == IsReadOnlyProperty)
		{
			PseudoClasses.Set(":readonly", change.GetNewValue<bool>());
		}
		else if (change.Property == ConversionRateProperty)
		{
			PseudoClasses.Set(":noexchangerate", change.GetNewValue<decimal>() == 0m);
		}
		else if (change.Property == IsConversionReversedProperty)
		{
			PseudoClasses.Set(":reversed", change.GetNewValue<bool>());
			ReorganizeVisuals();
			UpdateDisplay();
		}
	}

	// this is ugly, but I couldn't find another way to make tab key and automatic focus to work properly
	// setting Grid.Column via pseudoclass based style doesn't work, not even using AffectsMeasure()... Avalonia bug?
	private void ReorganizeVisuals()
	{
		if (LeftEntryBox is { } && RightEntryBox is { })
		{
			var grid = LeftEntryBox.FindAncestorOfType<Grid>();
			grid?.Children.Remove(LeftEntryBox);
			grid?.Children.Remove(RightEntryBox);

			if (IsConversionReversed)
			{
				grid?.Children.Add(RightEntryBox);
				grid?.Children.Add(LeftEntryBox);
			}
			else
			{
				grid?.Children.Add(LeftEntryBox);
				grid?.Children.Add(RightEntryBox);
			}
		}
	}
}

using System.Globalization;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Media;
using ReactiveUI;
using WalletWasabi.Fluent.Helpers;

namespace WalletWasabi.Fluent.TreeDataGrid;

public class TreeDataGridPrivacyTextCell : TreeDataGridCell
{
	private IDisposable? _subscription;
	private bool _isContentVisible = true;
	private string? _text;
	private FormattedText? _formattedText;
	private FormattedText? _privacyFormattedText;
	private int _numberOfPrivacyChars;
	private string? _privacyText;
	private Size? _availableSize;
	private bool _haveText;

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property == ForegroundProperty)
		{
			InvalidateVisual();
		}
	}

	public override void Realize(
		TreeDataGridElementFactory factory,
		ITreeDataGridSelectionInteraction? selection,
		ICell model,
		int columnIndex,
		int rowIndex)
	{
		var privacyTextCell = (PrivacyTextCell)model;
		var text = privacyTextCell.Value;

		_numberOfPrivacyChars = privacyTextCell.NumberOfPrivacyChars;
		_privacyText = new string('#', _numberOfPrivacyChars);
		_privacyFormattedText = null;

		if (_text != text)
		{
			_text = text;
			_haveText = !string.IsNullOrWhiteSpace(_text);
			_formattedText = null;

			if (_availableSize is not null)
			{
				_formattedText = CreateFormattedText(_availableSize.Value, _text);
			}
		}

		base.Realize(factory, selection, model, columnIndex, rowIndex);
	}

	public override void Unrealize()
	{
		_formattedText = null;
		_text = null;
		_haveText = false;
		base.Unrealize();
	}

	public override void Render(DrawingContext context)
	{
		context.FillRectangle(Brushes.Transparent, new Rect(new Point(), DesiredSize));

		var formattedText = !_isContentVisible
			? _privacyFormattedText
			: _haveText ? _formattedText : null;

		if (formattedText is null)
		{
			return;
		}

		var r = Bounds.CenterRect(new Rect(new Point(0, 0), new Size(formattedText.Width, formattedText.Height)));
		if (Foreground is { })
		{
			formattedText.SetForegroundBrush(Foreground);
		}

		context.DrawText(formattedText, new Point(0, r.Position.Y));
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);

		_subscription = PrivacyModeHelper.DelayedRevealAndHide(
				this.WhenAnyValue(x => x.IsPointerOver),
				Services.UiConfig.WhenAnyValue(x => x.PrivacyMode))
			.ObserveOn(RxApp.MainThreadScheduler)
			.Do(SetContentVisible)
			.Subscribe();
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		_subscription?.Dispose();
		_subscription = null;
	}

	protected override Size MeasureOverride(Size availableSize)
	{
		_availableSize = availableSize;

		if ((_formattedText is null && !string.IsNullOrWhiteSpace(_text))
		    || _privacyFormattedText is null
		    || (_availableSize is not null && _availableSize != availableSize))
		{
			_formattedText = !string.IsNullOrWhiteSpace(_text)
				? CreateFormattedText(availableSize, _text)
				: null;
			_privacyFormattedText = CreateFormattedText(availableSize, _privacyText);
		}

		if (_formattedText is null)
		{
			return new Size(
				_privacyFormattedText.Width,
				_privacyFormattedText.Height);
		}

		return new Size(
			Math.Max(_formattedText.Width, _privacyFormattedText.Width),
			Math.Max(_formattedText.Height, _privacyFormattedText.Height));
	}

	private void SetContentVisible(bool value)
	{
		_isContentVisible = value;
		InvalidateVisual();
	}

	private FormattedText CreateFormattedText(Size availableSize, string? text)
	{
		return new FormattedText(
			text ?? string.Empty,
			CultureInfo.CurrentCulture,
			FlowDirection.LeftToRight,
			new Typeface(FontFamily, FontStyle, FontWeight),
			FontSize,
			null)
		{
			TextAlignment = TextAlignment.Left,
			MaxTextHeight = availableSize.Height,
			MaxTextWidth = availableSize.Width,
			Trimming = TextTrimming.None
		};
	}
}

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WalletWasabi.Fluent.Controls;

public enum HighlightedButton
{
	None,
	YesButton,
	NoButton,
	Both
}

public class QuestionControl : ContentControl
{
	public static readonly StyledProperty<ICommand> YesCommandProperty =
		AvaloniaProperty.Register<QuestionControl, ICommand>(nameof(YesCommand));

	public static readonly StyledProperty<ICommand> NoCommandProperty =
		AvaloniaProperty.Register<QuestionControl, ICommand>(nameof(NoCommand));

	public static readonly StyledProperty<IImage> ImageIconProperty =
		AvaloniaProperty.Register<QuestionControl, IImage>(nameof(ImageIcon));

	public static readonly StyledProperty<object?> IconContentProperty =
		AvaloniaProperty.Register<QuestionControl, object?>(nameof(IconContent));

	public static readonly StyledProperty<string> YesContentProperty =
		AvaloniaProperty.Register<QuestionControl, string>(nameof(YesContent));

	public static readonly StyledProperty<string> NoContentProperty =
		AvaloniaProperty.Register<QuestionControl, string>(nameof(NoContent));

	public static readonly StyledProperty<HighlightedButton> HighlightButtonProperty =
		AvaloniaProperty.Register<QuestionControl, HighlightedButton>(nameof(HighlightButton));

	public static readonly StyledProperty<bool> IsYesButtonProperty =
		AvaloniaProperty.Register<QuestionControl, bool>(nameof(IsYesButton));

	public static readonly StyledProperty<bool> IsNoButtonProperty =
		AvaloniaProperty.Register<QuestionControl, bool>(nameof(IsNoButton));

	public QuestionControl()
	{
		UpdateHighlightedButton(HighlightButton);
		YesContent = "Yes";
		NoContent = "No";
	}

	public ICommand YesCommand
	{
		get => GetValue(YesCommandProperty);
		set => SetValue(YesCommandProperty, value);
	}

	public ICommand NoCommand
	{
		get => GetValue(NoCommandProperty);
		set => SetValue(NoCommandProperty, value);
	}

	public IImage ImageIcon
	{
		get => GetValue(ImageIconProperty);
		set => SetValue(ImageIconProperty, value);
	}

	private bool IsYesButton
	{
		get => GetValue(IsYesButtonProperty);
		set => SetValue(IsYesButtonProperty, value);
	}

	private bool IsNoButton
	{
		get => GetValue(IsNoButtonProperty);
		set => SetValue(IsNoButtonProperty, value);
	}

	public object? IconContent
	{
		get => GetValue(IconContentProperty);
		set => SetValue(IconContentProperty, value);
	}

	public string YesContent
	{
		get => GetValue(YesContentProperty);
		set => SetValue(YesContentProperty, value);
	}

	public string NoContent
	{
		get => GetValue(NoContentProperty);
		set => SetValue(NoContentProperty, value);
	}

	public HighlightedButton HighlightButton
	{
		get => GetValue(HighlightButtonProperty);
		set => SetValue(HighlightButtonProperty, value);
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property == HighlightButtonProperty)
		{
			UpdateHighlightedButton(change.GetNewValue<HighlightedButton>());
		}
	}

	private void UpdateHighlightedButton(HighlightedButton highlightedButton)
	{
		IsYesButton = highlightedButton is HighlightedButton.YesButton or HighlightedButton.Both;
		IsNoButton = highlightedButton is HighlightedButton.NoButton or HighlightedButton.Both;
	}
}

using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace WalletWasabi.Fluent.Controls;

public class TagsBoxAutoCompleteBox : AutoCompleteBox
{
	internal TagsBoxTextBox? InternalTextBox;
	internal ListBox? SuggestionListBox;

	protected override Type StyleKeyOverride => typeof(AutoCompleteBox);

	protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
	{
		base.OnApplyTemplate(e);

		InternalTextBox = e.NameScope.Find<TagsBoxTextBox>("PART_TextBox");
		SuggestionListBox = e.NameScope.Find<ListBox>("PART_SelectingItemsControl");
	}
}

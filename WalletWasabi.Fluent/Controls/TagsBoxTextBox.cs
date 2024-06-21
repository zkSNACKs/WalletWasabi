using Avalonia.Controls;
using Avalonia.Input;

namespace WalletWasabi.Fluent.Controls;

public class TagsBoxTextBox : TextBox
{
	protected override Type StyleKeyOverride => typeof(TextBox);

	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (string.IsNullOrEmpty(Text) && e.Key == Key.Back)
		{
			return;
		}

		base.OnKeyDown(e);
	}
}

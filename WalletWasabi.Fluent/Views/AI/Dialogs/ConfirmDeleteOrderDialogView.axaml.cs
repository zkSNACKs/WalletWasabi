using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace WalletWasabi.Fluent.Views.AI.Dialogs;

public class ConfirmDeleteChatDialogView : UserControl
{
	public ConfirmDeleteChatDialogView()
	{
		InitializeComponent();
	}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}
}

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace WalletWasabi.Fluent.Views.AddWallet.Create;

public class ConfirmRecoveryWordsView : UserControl
{
	public string MyEditableProperty { get; set; }
	public ConfirmRecoveryWordsView()
	{
		InitializeComponent();
	}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}
}

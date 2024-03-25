using WalletWasabi.Fluent.Infrastructure;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.ViewModels.Navigation;

namespace WalletWasabi.Fluent.ViewModels.Settings;

[AppLifetime]
[NavigationMetaData(
	Title = "Advanced",
	Caption = "Manage advanced settings",
	Order = 2,
	Category = "Settings",
	Keywords = new[]
	{
			"Settings", "Advanced", "Enable", "GPU"
	},
	IconName = "settings_general_regular")]
public partial class AdvancedSettingsTabViewModel : RoutableViewModel
{
	public AdvancedSettingsTabViewModel(IApplicationSettings settings)
	{
		Settings = settings;
	}

	public bool IsReadOnly => Settings.IsOverridden;

	public IApplicationSettings Settings { get; }
}

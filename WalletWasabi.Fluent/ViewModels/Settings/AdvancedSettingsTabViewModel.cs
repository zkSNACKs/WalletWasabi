using ReactiveUI;
using WalletWasabi.Fluent.Infrastructure;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.Validation;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Models;

namespace WalletWasabi.Fluent.ViewModels.Settings;

[AppLifetime]
[NavigationMetaData(
	Title = "Advanced",
	Caption = "Manage advanced settings",
	Order = 3,
	Category = "Settings",
	Keywords = new[]
	{
			"Settings", "Advanced", "Enable", "GPU", "Backend", "URI"
	},
	IconName = "settings_general_regular")]
public partial class AdvancedSettingsTabViewModel : RoutableViewModel
{
	[AutoNotify] private string _backendUri;

	public AdvancedSettingsTabViewModel(IApplicationSettings settings)
	{
		Settings = settings;
		_backendUri = settings.BackendUri;

		this.ValidateProperty(x => x.BackendUri, ValidateBackendUri);

		this.WhenAnyValue(x => x.Settings.BackendUri)
			.Subscribe(x => BackendUri = x);
	}

	public bool IsReadOnly => Settings.IsOverridden;

	public IApplicationSettings Settings { get; }

	private void ValidateBackendUri(IValidationErrors errors)
	{
		var backendUri = BackendUri;

		if (string.IsNullOrEmpty(backendUri))
		{
			return;
		}

		if (!Uri.TryCreate(backendUri, UriKind.Absolute, out _))
		{
			errors.Add(ErrorSeverity.Error, "Invalid URI.");
			return;
		}

		Settings.BackendUri = backendUri;
	}
}

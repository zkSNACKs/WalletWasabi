using System;
using System.Reactive.Linq;
using ReactiveUI;
using WalletWasabi.Fluent.ViewModels.NavBar;
using WalletWasabi.Helpers;

namespace WalletWasabi.Fluent.ViewModels.Settings
{
	[NavigationMetaData(Title = "Privacy Mode", Searchable = false, NavBarPosition = NavBarPosition.Bottom)]
	public partial class PrivacyModeViewModel : NavBarItemViewModel
	{
		[AutoNotify] private bool _privacyMode;

		public PrivacyModeViewModel()
		{
			Guard.NotNull(nameof(Services.UiConfig), Services.UiConfig);

			_privacyMode = Services.UiConfig.PrivacyMode;

			SelectionMode = NavBarItemSelectionMode.Toggle;

			ToggleTitle();

			this.WhenAnyValue(x => x.PrivacyMode)
				.Skip(1)
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Subscribe(
					x =>
				{
					ToggleTitle();
					this.RaisePropertyChanged(nameof(IconName));
					Services.UiConfig.PrivacyMode = x;
				});
		}

		public override string IconName => _privacyMode ? "privacy_mode_on" : "privacy_mode_off";

		public override void Toggle()
		{
			PrivacyMode = !PrivacyMode;
		}

		private void ToggleTitle()
		{
			Title = $"Privacy Mode {(_privacyMode ? "(On)" : "(Off)")}";
		}
	}
}
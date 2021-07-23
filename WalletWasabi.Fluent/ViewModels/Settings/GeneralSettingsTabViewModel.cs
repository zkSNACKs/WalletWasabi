using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using ReactiveUI;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Gui;
using WalletWasabi.Gui.Models;
using WalletWasabi.Logging;
using System.Windows.Input;
using DynamicData;

namespace WalletWasabi.Fluent.ViewModels.Settings
{
	[NavigationMetaData(
		Title = "General",
		Caption = "Manage general settings",
		Order = 0,
		Category = "Settings",
		Keywords = new[]
		{
			"Settings", "General", "Dark", "Mode", "Bitcoin", "Addresses", "Manual", "Entry", "Fee", "Custom", "Change",
			"Address", "Display", "Format", "Dust", "Threshold", "BTC", "Start", "System"
		},
		IconName = "settings_general_regular")]
	public partial class GeneralSettingsTabViewModel : SettingsTabViewModelBase
	{
		[AutoNotify] private bool _darkModeEnabled;
		[AutoNotify] private bool _autoCopy;
		[AutoNotify] private bool _customFee;
		[AutoNotify] private bool _customChangeAddress;
		[AutoNotify] private FeeDisplayFormat _selectedFeeDisplayFormat;
		[AutoNotify] private bool _runOnSystemStartup;

		public GeneralSettingsTabViewModel()
		{
			_darkModeEnabled = Services.UiConfig.DarkModeEnabled;
			_autoCopy = Services.UiConfig.Autocopy;
			_customFee = Services.UiConfig.IsCustomFee;
			_customChangeAddress = Services.UiConfig.IsCustomChangeAddress;
			_runOnSystemStartup = Services.UiConfig.RunOnSystemStartup;
			_selectedFeeDisplayFormat = Enum.IsDefined(typeof(FeeDisplayFormat), Services.UiConfig.FeeDisplayFormat)
				? (FeeDisplayFormat)Services.UiConfig.FeeDisplayFormat
				: FeeDisplayFormat.SatoshiPerByte;

			this.WhenAnyValue(x => x.DarkModeEnabled)
				.Skip(1)
				.Subscribe(
					x =>
					{
						Services.UiConfig.DarkModeEnabled = x;
						Navigate(NavigationTarget.CompactDialogScreen).To(new ThemeChangeViewModel(x ? Theme.Dark : Theme.Light));
					});

			this.WhenAnyValue(x => x.AutoCopy)
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Skip(1)
				.Subscribe(x => Services.UiConfig.Autocopy = x);

			StartUpCommand = ReactiveCommand.Create(async () =>
			{
				try
				{
					await StartupHelper.ModifyStartupSettingAsync(RunOnSystemStartup);
					Services.UiConfig.RunOnSystemStartup = RunOnSystemStartup;
				}
				catch (Exception ex)
				{
					Logger.LogError(ex);
					RunOnSystemStartup = !RunOnSystemStartup;
					await ShowErrorAsync(Title, "Couldn't save your change, please see the logs for further information.", "Error occured.");
				}
			});

			this.WhenAnyValue(x => x.CustomFee)
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Skip(1)
				.Subscribe(x => Services.UiConfig.IsCustomFee = x);

			this.WhenAnyValue(x => x.CustomChangeAddress)
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Skip(1)
				.Subscribe(x => Services.UiConfig.IsCustomChangeAddress = x);

			this.WhenAnyValue(x => x.SelectedFeeDisplayFormat)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Skip(1)
				.Subscribe(x => Services.UiConfig.FeeDisplayFormat = (int)x);
		}

		public ICommand StartUpCommand { get; }

		public IEnumerable<FeeDisplayFormat> FeeDisplayFormats =>
			Enum.GetValues(typeof(FeeDisplayFormat)).Cast<FeeDisplayFormat>();

		protected override void EditConfigOnSave(Config config)
		{
		}
	}
}

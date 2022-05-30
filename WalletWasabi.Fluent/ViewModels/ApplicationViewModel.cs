using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using ReactiveUI;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Providers;
using WalletWasabi.Fluent.ViewModels.Dialogs;
using WalletWasabi.Fluent.ViewModels.HelpAndSupport;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.WabiSabi.Client;

namespace WalletWasabi.Fluent.ViewModels;

public partial class ApplicationViewModel : ViewModelBase, ICanShutdownProvider
{
	private readonly IMainWindowService _mainWindowService;
	[AutoNotify] private bool _isMainWindowShown = true;

	public ApplicationViewModel(IMainWindowService mainWindowService)
	{
		_mainWindowService = mainWindowService;

		QuitCommand = ReactiveCommand.Create(ShutDown);

		ShowHideCommand = ReactiveCommand.Create(() =>
		{
			if (IsMainWindowShown)
			{
				_mainWindowService.Close();
			}
			else
			{
				_mainWindowService.Show();
			}
		});

		ShowCommand = ReactiveCommand.Create(() => _mainWindowService.Show());

		AboutCommand = ReactiveCommand.Create(
			() => MainViewModel.Instance.Value.DialogScreen.To(new AboutViewModel(navigateBack: MainViewModel.Instance.Value.DialogScreen.CurrentPage is not null)),
			canExecute: MainViewModel.Instance.Value.DialogScreen.WhenAnyValue(x => x.CurrentPage).Select(x => x is null));

		using var bitmap = AssetHelpers.GetBitmapAsset("avares://WalletWasabi.Fluent/Assets/WasabiLogo.ico");
		TrayIcon = new WindowIcon(bitmap);
	}

	public WindowIcon TrayIcon { get; }
	public ICommand AboutCommand { get; }
	public ICommand ShowCommand { get; }

	public ICommand ShowHideCommand { get; }

	public ICommand QuitCommand { get; }

	public void ShutDown() => _mainWindowService.Shutdown();

	public void OnShutdownPrevented()
	{
		RxApp.MainThreadScheduler.Schedule(() => MainViewModel.Instance.Value.CompactDialogScreen.To(new ShuttingDownViewModel(this)));
	}

	public bool CanShutdown()
	{
		var cjManager = Services.HostedServices.GetOrDefault<CoinJoinManager>();

		if (cjManager is { })
		{
			return cjManager.HighestCoinJoinClientState switch
			{
				CoinJoinClientState.InCriticalPhase => false,
				CoinJoinClientState.Idle or CoinJoinClientState.InProgress => true,
				_ => throw new ArgumentOutOfRangeException(),
			};
		}

		return true;
	}
}

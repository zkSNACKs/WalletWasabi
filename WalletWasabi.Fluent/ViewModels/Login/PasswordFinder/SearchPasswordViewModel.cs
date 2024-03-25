using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Logging;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Fluent.Models.Wallets;
using System.Reactive.Linq;

namespace WalletWasabi.Fluent.ViewModels.Login.PasswordFinder;

[NavigationMetaData(Title = "Password Finder")]
public partial class SearchPasswordViewModel : RoutableViewModel
{
	private readonly IPasswordFinderModel _model;
	[AutoNotify] private int _percentage;
	[AutoNotify] private int _remainingHour;
	[AutoNotify] private int _remainingMin;
	[AutoNotify] private int _remainingSec;
	[AutoNotify] private string _hourText;
	[AutoNotify] private string _minText;
	[AutoNotify] private string _secText;
	[AutoNotify] private bool _remainingTimeReceived;

	private SearchPasswordViewModel(IPasswordFinderModel model)
	{
		_model = model;
		_hourText = "";
		_minText = "";
		_secText = "";

		SetupCancel(enableCancel: true, enableCancelOnEscape: true, enableCancelOnPressed: true);

		EnableBack = false;
	}

	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		base.OnNavigatedTo(isInHistory, disposables);

		var cts = new CancellationTokenSource()
			.DisposeWith(disposables);

		_model.Progress
			.Do(t => SetStatus(t.Percentage, t.RemainingTime))
			.Subscribe()
			.DisposeWith(disposables);

		var t = FindPasswordAsync(cts.Token);

		Disposable.Create(
				async () =>
				{
					cts.Cancel();
					await t;
				})
			.DisposeWith(disposables);
	}

	private async Task FindPasswordAsync(CancellationToken token)
	{
		try
		{
			var (result, foundPassword) = await _model.FindPasswordAsync(token);
			if (result && foundPassword is { })
			{
				UiContext.Navigate().To().PasswordFound(foundPassword, navigationMode: NavigationMode.Clear);
			}
			else
			{
				UiContext.Navigate().To().PasswordNotFound(_model.Wallet, navigationMode: NavigationMode.Clear);
			}
		}
		catch (OperationCanceledException)
		{
			Logger.LogTrace("Operation was canceled.");
		}
	}

	private void SetStatus(int percentage, TimeSpan remainingTime)
	{
		RemainingTimeReceived = true;
		Percentage = percentage;

		RemainingHour = remainingTime.Hours;
		RemainingMin = remainingTime.Minutes;
		RemainingSec = remainingTime.Seconds;

		HourText = $" hour{TextHelpers.AddSIfPlural(RemainingHour)}{TextHelpers.CloseSentenceIfZero(RemainingMin, RemainingSec)}";
		MinText = $" minute{TextHelpers.AddSIfPlural(RemainingMin)}{TextHelpers.CloseSentenceIfZero(RemainingSec)}";
		SecText = $" second{TextHelpers.AddSIfPlural(RemainingSec)}.";
	}
}

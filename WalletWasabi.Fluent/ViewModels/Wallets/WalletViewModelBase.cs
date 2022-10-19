using ReactiveUI;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.ViewModels.NavBar;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Helpers;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets;

public abstract partial class WalletViewModelBase : NavBarItemViewModel, IComparable<WalletViewModelBase>
{
	[AutoNotify(SetterModifier = AccessModifier.Protected)] private bool _isLoading;
	[AutoNotify(SetterModifier = AccessModifier.Protected)] private bool _isCoinJoining;
	[AutoNotify(SetterModifier = AccessModifier.Private)] private WalletState _walletState;
	[AutoNotify] private string _walletName;
	private string _title;

	protected WalletViewModelBase(Wallet wallet)
	{
		Wallet = Guard.NotNull(nameof(wallet), wallet);
		_walletName = Wallet.WalletName;
		_title = WalletName;
		WalletState = wallet.State;

		OpenCommand = ReactiveCommand.Create(() => Navigate().To(this, NavigationMode.Clear));

		SetIcon();

		this.WhenAnyValue(x => x.IsCoinJoining)
			.Skip(1)
			.Subscribe(_ => MainViewModel.Instance.InvalidateIsCoinJoinActive());

		this.WhenAnyValue(x => x.WalletName)
			.Do(x => Wallet.WalletName = x)
			.Do(x => Title = x)
			.Subscribe();
	}

	public override string Title
	{
		get => _title;
		protected set => this.RaiseAndSetIfChanged(ref _title, value);
	}

	public Wallet Wallet { get; }

	public bool IsLoggedIn => Wallet.IsLoggedIn;

	public bool PreferPsbtWorkflow => Wallet.KeyManager.PreferPsbtWorkflow;

	private void SetIcon()
	{
		var walletType = WalletHelpers.GetType(Wallet.KeyManager);

		var baseResourceName = walletType switch
		{
			WalletType.Coldcard => "coldcard_24",
			WalletType.Trezor => "trezor_24",
			WalletType.Ledger => "ledger_24",
			_ => "wallet_24"
		};

		IconName = $"nav_{baseResourceName}_regular";
		IconNameFocused = $"nav_{baseResourceName}_filled";
	}

	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		base.OnNavigatedTo(isInHistory, disposables);

		Observable.FromEventPattern<WalletState>(Wallet, nameof(Wallet.StateChanged))
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(x => WalletState = x.EventArgs)
			.DisposeWith(disposables);
	}

	public int CompareTo(WalletViewModelBase? other)
	{
		if (other is null)
		{
			return -1;
		}

		var result = other.IsLoggedIn.CompareTo(IsLoggedIn);

		if (result == 0)
		{
			result = string.Compare(Title, other.Title, StringComparison.Ordinal);
		}

		return result;
	}

	public override string ToString() => WalletName;
}

using DynamicData;
using NBitcoin;
using ReactiveUI;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Helpers;
using WalletWasabi.Hwi.Models;
using WalletWasabi.Models;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.Models.Wallets;

[AutoInterface]
public partial class WalletRepository : ReactiveObject
{
	private readonly IAmountProvider _amountProvider;
	private readonly Dictionary<WalletId, WalletModel> _walletDictionary = new();
	private readonly CompositeDisposable _disposable = new();

	public WalletRepository(IAmountProvider amountProvider)
	{
		_amountProvider = amountProvider;

		var signals =
			Observable.FromEventPattern<Wallet>(Services.WalletManager, nameof(WalletManager.WalletAdded))
					  .Select(_ => System.Reactive.Unit.Default)
					  .StartWith(System.Reactive.Unit.Default);

		Wallets =
			signals.Fetch(() => Services.WalletManager.GetWallets(), x => x.WalletId, new LambdaComparer<Wallet>((a, b) => Equals(a?.WalletId, b?.WalletId)))
				   .DisposeWith(_disposable)
				   .Connect()
				   .TransformWithInlineUpdate(CreateWalletModel, (_, _) => { })
				   .Cast(x => (IWalletModel)x)
				   .AsObservableCache()
				   .DisposeWith(_disposable);

		DefaultWalletName = Services.UiConfig.LastSelectedWallet;
	}

	public IObservableCache<IWalletModel, WalletId> Wallets { get; }

	public string? DefaultWalletName { get; }
	public bool HasWallet => Services.WalletManager.HasWallet();

	private KeyPath AccountKeyPath { get; } = KeyManager.GetAccountKeyPath(Services.WalletManager.Network, ScriptPubKeyType.Segwit);

	public void StoreLastSelectedWallet(IWalletModel wallet)
	{
		Services.UiConfig.LastSelectedWallet = wallet.Name;
	}

	public string GetNextWalletName()
	{
		return Services.WalletManager.WalletDirectories.GetNextWalletName("Wallet");
	}

	public async Task<IWalletSettingsModel> NewWalletAsync(WalletCreationOptions options, CancellationToken? cancelToken = null)
	{
		return options switch
		{
			WalletCreationOptions.AddNewWallet add => await CreateNewWalletAsync(add),
			WalletCreationOptions.ConnectToHardwareWallet hw => await ConnectToHardwareWalletAsync(hw, cancelToken),
			WalletCreationOptions.ImportWallet import => await ImportWalletAsync(import),
			WalletCreationOptions.RecoverWallet recover => await RecoverWalletAsync(recover),
			_ => throw new InvalidOperationException($"{nameof(WalletCreationOptions)} not supported: {options?.GetType().Name}")
		};
	}

	public IWalletModel SaveWallet(IWalletSettingsModel walletSettings)
	{
		var id = walletSettings.Save();
		var result = GetById(id);
		result.Settings.IsCoinJoinPaused = walletSettings.IsCoinJoinPaused;
		return result;
	}

	public (ErrorSeverity Severity, string Message)? ValidateWalletName(string walletName)
	{
		return Services.WalletManager.ValidateWalletName(walletName);
	}

	public IWalletModel? GetExistingWallet(HwiEnumerateEntry device)
	{
		var existingWallet = Services.WalletManager.GetWallets().FirstOrDefault(x => x.KeyManager.MasterFingerprint == device.Fingerprint);
		if (existingWallet is { })
		{
			return GetById(existingWallet.WalletId);
		}
		return null;
	}

	private async Task<IWalletSettingsModel> CreateNewWalletAsync(WalletCreationOptions.AddNewWallet options)
	{
		var (walletName, password, mnemonic) = options;

		ArgumentException.ThrowIfNullOrEmpty(walletName);
		ArgumentNullException.ThrowIfNull(password);

		var (keyManager, _) = await Task.Run(
				() =>
				{
					var walletGenerator = new WalletGenerator(
						Services.WalletManager.WalletDirectories.WalletsDir,
						Services.WalletManager.Network)
					{
						TipHeight = Services.SmartHeaderChain.TipHeight
					};
					return walletGenerator.GenerateWallet(walletName, password, mnemonic);
				});

		return new WalletSettingsModel(keyManager, true);
	}

	private async Task<IWalletSettingsModel> ConnectToHardwareWalletAsync(WalletCreationOptions.ConnectToHardwareWallet options, CancellationToken? cancelToken)
	{
		var (walletName, device) = options;

		ArgumentException.ThrowIfNullOrEmpty(walletName);
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(cancelToken);

		var walletFilePath = Services.WalletManager.WalletDirectories.GetWalletFilePaths(walletName).walletFilePath;
		var keyManager = await HardwareWalletOperationHelpers.GenerateWalletAsync(device, walletFilePath, Services.WalletManager.Network, cancelToken.Value);
		keyManager.SetIcon(device.WalletType);

		var result = new WalletSettingsModel(keyManager, true);
		return result;
	}

	private async Task<IWalletSettingsModel> ImportWalletAsync(WalletCreationOptions.ImportWallet options)
	{
		var (walletName, filePath) = options;

		ArgumentException.ThrowIfNullOrEmpty(walletName);
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		var keyManager = await ImportWalletHelper.ImportWalletAsync(Services.WalletManager, walletName, filePath);
		return new WalletSettingsModel(keyManager, true);
	}

	private async Task<IWalletSettingsModel> RecoverWalletAsync(WalletCreationOptions.RecoverWallet options)
	{
		var (walletName, password, mnemonic, minGapLimit, path) = options;

		ArgumentException.ThrowIfNullOrEmpty(walletName);
		ArgumentNullException.ThrowIfNull(password);
		ArgumentNullException.ThrowIfNull(mnemonic);
		ArgumentNullException.ThrowIfNull(minGapLimit);

		var accountKeyPath = AccountKeyPath;

		if (!string.IsNullOrEmpty(path))
		{
			if (KeyPath.TryParse(path, out var userKeyPath) && userKeyPath is not null)
			{
				accountKeyPath = userKeyPath;
			}
			else
			{
				throw new InvalidOperationException("Could not parse key path.");
			}
		}

		var keyManager = await Task.Run(() =>
		{
			var walletFilePath = Services.WalletManager.WalletDirectories.GetWalletFilePaths(walletName).walletFilePath;

			var result = KeyManager.Recover(
				mnemonic,
				password,
				Services.WalletManager.Network,
				accountKeyPath,
				null,
				"", // Make sure it is not saved into a file yet.
				minGapLimit.Value);

			result.AutoCoinJoin = true;

			// Set the filepath but we will only write the file later when the Ui workflow is done.
			result.SetFilePath(walletFilePath);

			return result;
		});

		return new WalletSettingsModel(keyManager, true, true);
	}

	private IWalletModel GetById(WalletId id)
	{
		return
			_walletDictionary.TryGetValue(id, out var wallet)
			? wallet
			: throw new InvalidOperationException($"Wallet not found: {id}");
	}

	private WalletModel CreateWalletModel(Wallet wallet)
	{
		if (_walletDictionary.TryGetValue(wallet.WalletId, out var existing))
		{
			if (!ReferenceEquals(existing.Wallet, wallet))
			{
				throw new InvalidOperationException($"Different instance of: {wallet.WalletName}");
			}
			return existing;
		}

		var result =
			wallet.KeyManager.IsHardwareWallet
			? new HardwareWalletModel(wallet, _amountProvider)
			: new WalletModel(wallet, _amountProvider);

		_walletDictionary[wallet.WalletId] = result;

		return result;
	}
}

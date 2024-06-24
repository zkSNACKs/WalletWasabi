using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData;
using ReactiveUI;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.ViewModels.Wallets.Send;
using WalletWasabi.Logging;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.Models.Wallets;

[AutoInterface]
public partial class WalletCoinsModel : IDisposable
{
	private readonly Wallet _wallet;
	private readonly IWalletModel _walletModel;
	private readonly CompositeDisposable _disposables = new();

	public WalletCoinsModel(Wallet wallet, IWalletModel walletModel)
	{
		_wallet = wallet;
		_walletModel = walletModel;
		var transactionProcessed = walletModel.Transactions.TransactionProcessed;
		var anonScoreTargetChanged = this.WhenAnyValue(x => x._walletModel.Settings.AnonScoreTarget).Skip(1).ToSignal();
		var isCoinjoinRunningChanged = walletModel.Coinjoin.IsRunning.ToSignal();

		var signals =
			transactionProcessed
				.Merge(anonScoreTargetChanged)
				.Merge(isCoinjoinRunningChanged)
				.Publish();

		Pockets = signals.Fetch(GetPockets, x => x.Labels, new LambdaComparer<Pocket>((a, b) => Equals(a?.Labels, b?.Labels))).DisposeWith(_disposables);
		List = signals.Fetch(GetCoins, x => x.Key, new LambdaComparer<ICoinModel>((a, b) => Equals(a?.Key, b?.Key)));

		signals
			.Do(_ => Logger.LogDebug($"Refresh signal emitted in {walletModel.Name}"))
			.Subscribe()
			.DisposeWith(_disposables);

		signals.Connect()
			.DisposeWith(_disposables);
	}

	public IObservableCache<ICoinModel, int> List { get; }

	public IObservableCache<Pocket, LabelsArray> Pockets { get; }

	public async Task UpdateExcludedCoinsFromCoinjoinAsync(ICoinModel[] coinsToExclude)
	{
		await Task.Run(() =>
		{
			// TODO: To keep models in sync with business objects. Should be automatic.
			foreach (var coinModel in List.Items)
			{
				coinModel.IsExcludedFromCoinJoin = coinsToExclude.Any(x => x.IsSame(coinModel));
			}

			var outPoints = coinsToExclude.Select(x => x.GetSmartCoin().Outpoint).ToArray();
			_wallet.UpdateExcludedCoinsFromCoinJoin(outPoints);
		});
	}

	public List<ICoinModel> GetSpentCoins(BuildTransactionResult? transaction)
	{
		var coins = (transaction?.SpentCoins ?? new List<SmartCoin>()).ToList();
		return coins.Select(GetCoinModel).ToList();
	}

	public ICoinModel GetCoinModel(SmartCoin smartCoin)
	{
		return new CoinModel(smartCoin, _walletModel.Settings.AnonScoreTarget);
	}

	public bool AreEnoughToCreateTransaction(TransactionInfo transactionInfo, IEnumerable<ICoinModel> coins)
	{
		return TransactionHelpers.TryBuildTransactionWithoutPrevTx(_wallet.KeyManager, transactionInfo, _wallet.Coins, coins.GetSmartCoins(), _wallet.Kitchen.SaltSoup(), out _);
	}

	private Pocket[] GetPockets()
	{
		return _wallet.GetPockets().ToArray();
	}

	private ICoinModel[] GetCoins()
	{
		return _wallet.Coins.Select(GetCoinModel).ToArray();
	}

	public void Dispose() => _disposables.Dispose();
}

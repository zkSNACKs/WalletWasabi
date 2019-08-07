using NBitcoin;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using WalletWasabi.Logging;
using WalletWasabi.Models;

namespace WalletWasabi.Gui.Controls.WalletExplorer
{
	public class HistoryTabViewModel : WalletActionViewModel
	{
		private CompositeDisposable Disposables { get; set; }

		private ObservableCollection<TransactionViewModel> _transactions;
		private TransactionViewModel _selectedTransaction;
		private SortOrder _dateSortDirection;
		private SortOrder _amountSortDirection;
		private SortOrder _transactionSortDirection;
		private SortOrder _labelSortDirection;
		private bool _isFirstLoading;

		public bool IsFirstLoading
		{
			get => _isFirstLoading;
			set => this.RaiseAndSetIfChanged(ref _isFirstLoading, value);
		}

		public ReactiveCommand<Unit, Unit> SortCommand { get; }
		public ReactiveCommand<Unit, Unit> CopySelectedTxIdCommand { get; }

		public HistoryTabViewModel(WalletViewModel walletViewModel)
			: base("History", walletViewModel)
		{
			IsFirstLoading = true;

			Transactions = new ObservableCollection<TransactionViewModel>();

			SortCommand = ReactiveCommand.Create(RefreshOrdering);

			this.WhenAnyValue(x => x.SelectedTransaction).Subscribe(_ =>
			{
				this.RaisePropertyChanged(nameof(IsTxSelected));
				this.RaisePropertyChanged(nameof(SelectedTxId));
				this.RaisePropertyChanged(nameof(SelectedTxConfirmations));
				this.RaisePropertyChanged(nameof(SelectedTxBlockHeight));
				this.RaisePropertyChanged(nameof(SelectedTxSize));
				this.RaisePropertyChanged(nameof(SelectedTxVirtualSize));
				this.RaisePropertyChanged(nameof(SelectedTxFees));
				this.RaisePropertyChanged(nameof(SelectedTxRBF));
			});

			CopySelectedTxIdCommand = ReactiveCommand.CreateFromTask(async () =>
			{
				if (Global.UiConfig?.Autocopy is true && SelectedTransaction != null)
				{
					await SelectedTransaction.TryCopyTxIdToClipboardAsync();
				}
			});

			DateSortDirection = SortOrder.Decreasing;

			_ = TryRewriteTableAsync();
		}

		public override void OnOpen()
		{
			base.OnOpen();

			Disposables = Disposables is null ? new CompositeDisposable() : throw new NotSupportedException($"Cannot open {GetType().Name} before closing it.");

			Observable.FromEventPattern(Global.WalletService.Coins, nameof(Global.WalletService.Coins.CollectionChanged))
				.Merge(Observable.FromEventPattern(Global.WalletService, nameof(Global.WalletService.NewBlockProcessed)))
				.Merge(Observable.FromEventPattern(Global.WalletService, nameof(Global.WalletService.CoinSpentOrSpenderConfirmed)))
				.Throttle(TimeSpan.FromSeconds(5))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(async _ => await TryRewriteTableAsync())
				.DisposeWith(Disposables);

			Global.UiConfig.WhenAnyValue(x => x.LurkingWifeMode).ObserveOn(RxApp.MainThreadScheduler).Subscribe(x =>
			{
				foreach (var transaction in Transactions)
				{
					transaction.Refresh();
				}
			}).DisposeWith(Disposables);
		}

		public override bool OnClose()
		{
			Disposables.Dispose();
			Disposables = null;

			return base.OnClose();
		}

		private async Task TryRewriteTableAsync()
		{
			try
			{
				var txRecordList = await Task.Run(BuildTxRecordList);

				var rememberSelectedTransactionId = SelectedTransaction?.TransactionId;
				Transactions?.Clear();

				var trs = txRecordList.Select(txr => new TransactionInfo
				{
					DateTime = txr.dateTime.ToLocalTime(),
					Confirmed = txr.height.Type == HeightType.Chain,
					Confirmations = txr.height.Type == HeightType.Chain ? Global.BitcoinStore.HashChain.TipHeight - txr.height.Value + 1 : 0,
					BlockHeight = txr.height.Type == HeightType.Chain ? txr.height.Value : 0,
					AmountBtc = $"{txr.amount.ToString(fplus: true, trimExcessZero: true)}",
					Label = txr.label,
					TransactionId = txr.transactionId.ToString(),
					Size = txr.size,
					VirtualSize = txr.vSize,
					RBF = txr.rbf,
					Fees = txr.fees,
				}).Select(ti => new TransactionViewModel(ti));

				Transactions = new ObservableCollection<TransactionViewModel>(trs);

				if (Transactions.Count > 0 && !(rememberSelectedTransactionId is null))
				{
					var txToSelect = Transactions.FirstOrDefault(x => x.TransactionId == rememberSelectedTransactionId);
					if (txToSelect != default)
					{
						SelectedTransaction = txToSelect;
					}
				}
				RefreshOrdering();
			}
			catch (Exception ex)
			{
				Logger.LogError<HistoryTabViewModel>($"Error while RewriteTable on HistoryTab:  {ex}.");
			}
			finally
			{
				IsFirstLoading = false;
			}
		}

		private List<(DateTimeOffset dateTime, Height height, Money amount, string label, uint256 transactionId, int size, int vSize, bool rbf, string fees)> BuildTxRecordList()
		{
			var walletService = Global.WalletService;

			List<Transaction> trs = new List<Transaction>();
			var txRecordList = new List<(DateTimeOffset dateTime, Height height, Money amount, string label, uint256 transactionId, int size, int vSize, bool rbf, string fees)>();

			if (walletService is null)
			{
				return txRecordList;
			}

			foreach (SmartCoin coin in walletService.Coins)
			{
				var found = txRecordList.FirstOrDefault(x => x.transactionId == coin.TransactionId);

				SmartTransaction foundTransaction = walletService.TransactionCache?.FirstOrDefault(x => x.GetHash() == coin.TransactionId);
				if (foundTransaction is null)
				{
					continue;
				}

				DateTimeOffset dateTime;
				if (foundTransaction.Height.Type == HeightType.Chain)
				{
					if (walletService.ProcessedBlocks.Any(x => x.Value.height == foundTransaction.Height))
					{
						dateTime = walletService.ProcessedBlocks.First(x => x.Value.height == foundTransaction.Height).Value.dateTime;
					}
					else
					{
						dateTime = DateTimeOffset.UtcNow;
					}
				}
				else
				{
					dateTime = foundTransaction.FirstSeenIfMempoolTime ?? DateTimeOffset.UtcNow;
				}

				Dictionary<Coin, SmartCoin> spentCoins = WalletService.Coins.Where(x => foundTransaction.Transaction.Inputs.Any(y => y.PrevOut.Hash == x.TransactionId && y.PrevOut.N == x.Index)).ToDictionary(x => x.GetCoin());
				long satFee = foundTransaction.Transaction.GetFee(spentCoins.Keys.ToArray())?.Satoshi ?? -1;
				string fees = satFee < 0 ? "N/A" : satFee + " sats (" + foundTransaction.Transaction.GetFeeRate(spentCoins.Keys.ToArray())?.SatoshiPerByte + " sat/vbyte)";

				if (found != default) // if found
				{
					txRecordList.Remove(found);
					var foundLabel = found.label != string.Empty ? found.label + ", " : "";
					txRecordList.Add((dateTime, found.height, found.amount + coin.Amount, $"{foundLabel}{coin.Label}", coin.TransactionId, foundTransaction.Transaction.GetSerializedSize(), foundTransaction.Transaction.GetVirtualSize(), foundTransaction.IsRBF, fees));
				}
				else
				{
					txRecordList.Add((dateTime, coin.Height, coin.Amount, coin.Label, coin.TransactionId, foundTransaction.Transaction.GetSerializedSize(), foundTransaction.Transaction.GetVirtualSize(), foundTransaction.IsRBF, fees));
				}

				if (coin.SpenderTransactionId != null)
				{
					SmartTransaction foundSpenderTransaction = walletService.TransactionCache.First(x => x.GetHash() == coin.SpenderTransactionId);

					if (foundSpenderTransaction.Height.Type == HeightType.Chain)
					{
						if (walletService.ProcessedBlocks != null) // NullReferenceException appeared here.
						{
							if (walletService.ProcessedBlocks.Any(x => x.Value.height == foundSpenderTransaction.Height))
							{
								if (walletService.ProcessedBlocks != null) // NullReferenceException appeared here.
								{
									dateTime = walletService.ProcessedBlocks.First(x => x.Value.height == foundSpenderTransaction.Height).Value.dateTime;
								}
								else
								{
									dateTime = DateTimeOffset.UtcNow;
								}
							}
							else
							{
								dateTime = DateTimeOffset.UtcNow;
							}
						}
						else
						{
							dateTime = DateTimeOffset.UtcNow;
						}
					}
					else
					{
						dateTime = foundSpenderTransaction.FirstSeenIfMempoolTime ?? DateTimeOffset.UtcNow;
					}

					spentCoins = WalletService.Coins.Where(x => foundSpenderTransaction.Transaction.Inputs.Any(y => y.PrevOut.Hash == x.TransactionId && y.PrevOut.N == x.Index)).ToDictionary(x => x.GetCoin());
					satFee = foundSpenderTransaction.Transaction.GetFee(spentCoins.Keys.ToArray())?.Satoshi ?? -1;
					fees = satFee < 0 ? "N/A" : satFee + " sats (" + foundSpenderTransaction.Transaction.GetFeeRate(spentCoins.Keys.ToArray())?.SatoshiPerByte + " sat/vbyte)";

					var foundSpenderCoin = txRecordList.FirstOrDefault(x => x.transactionId == coin.SpenderTransactionId);
					if (foundSpenderCoin != default) // if found
					{
						txRecordList.Remove(foundSpenderCoin);
						txRecordList.Add((dateTime, foundSpenderTransaction.Height, foundSpenderCoin.amount - coin.Amount, foundSpenderCoin.label, coin.SpenderTransactionId, foundSpenderTransaction.Transaction.GetSerializedSize(), foundSpenderTransaction.Transaction.GetVirtualSize(), foundSpenderTransaction.IsRBF, fees));
					}
					else
					{
						txRecordList.Add((dateTime, foundSpenderTransaction.Height, (Money.Zero - coin.Amount), "", coin.SpenderTransactionId, foundSpenderTransaction.Transaction.GetSerializedSize(), foundSpenderTransaction.Transaction.GetVirtualSize(), foundSpenderTransaction.IsRBF, fees));
					}
				}
			}
			txRecordList = txRecordList.OrderByDescending(x => x.dateTime).ThenBy(x => x.amount).ToList();
			return txRecordList;
		}

		public ObservableCollection<TransactionViewModel> Transactions
		{
			get => _transactions;
			set => this.RaiseAndSetIfChanged(ref _transactions, value);
		}

		public bool IsTxSelected => SelectedTransaction != null;

		public string SelectedTxId => SelectedTransaction?.TransactionId ?? "N/A";

		public int SelectedTxConfirmations => SelectedTransaction?.Confirmations ?? 0;

		public int SelectedTxBlockHeight => SelectedTransaction?.BlockHeight ?? 0;

		public int SelectedTxSize => SelectedTransaction?.Size ?? 0;

		public int SelectedTxVirtualSize => SelectedTransaction?.VirtualSize ?? 0;

		public string SelectedTxFees => SelectedTransaction?.Fees ?? "N/A";

		public bool SelectedTxRBF => SelectedTransaction?.RBF ?? false;

		public TransactionViewModel SelectedTransaction
		{
			get => _selectedTransaction;
			set => this.RaiseAndSetIfChanged(ref _selectedTransaction, value);
		}

		public SortOrder DateSortDirection
		{
			get => _dateSortDirection;
			set
			{
				this.RaiseAndSetIfChanged(ref _dateSortDirection, value);
				if (value != SortOrder.None)
				{
					AmountSortDirection = SortOrder.None;
					TransactionSortDirection = SortOrder.None;
					LabelSortDirection = SortOrder.None;
				}
			}
		}

		public SortOrder AmountSortDirection
		{
			get => _amountSortDirection;
			set
			{
				this.RaiseAndSetIfChanged(ref _amountSortDirection, value);
				if (value != SortOrder.None)
				{
					DateSortDirection = SortOrder.None;
					TransactionSortDirection = SortOrder.None;
					LabelSortDirection = SortOrder.None;
				}
			}
		}

		public SortOrder TransactionSortDirection
		{
			get => _transactionSortDirection;
			set
			{
				this.RaiseAndSetIfChanged(ref _transactionSortDirection, value);
				if (value != SortOrder.None)
				{
					AmountSortDirection = SortOrder.None;
					DateSortDirection = SortOrder.None;
					LabelSortDirection = SortOrder.None;
				}
			}
		}

		public SortOrder LabelSortDirection
		{
			get => _labelSortDirection;
			set
			{
				this.RaiseAndSetIfChanged(ref _labelSortDirection, value);
				if (value != SortOrder.None)
				{
					AmountSortDirection = SortOrder.None;
					DateSortDirection = SortOrder.None;
					TransactionSortDirection = SortOrder.None;
				}
			}
		}

		private void RefreshOrdering()
		{
			if (TransactionSortDirection != SortOrder.None)
			{
				switch (TransactionSortDirection)
				{
					case SortOrder.Increasing:
						Transactions = new ObservableCollection<TransactionViewModel>(_transactions.OrderBy(t => t.TransactionId));
						break;

					case SortOrder.Decreasing:
						Transactions = new ObservableCollection<TransactionViewModel>(_transactions.OrderByDescending(t => t.TransactionId));
						break;
				}
			}
			else if (AmountSortDirection != SortOrder.None)
			{
				switch (AmountSortDirection)
				{
					case SortOrder.Increasing:
						Transactions = new ObservableCollection<TransactionViewModel>(_transactions.OrderBy(t => t.Amount));
						break;

					case SortOrder.Decreasing:
						Transactions = new ObservableCollection<TransactionViewModel>(_transactions.OrderByDescending(t => t.Amount));
						break;
				}
			}
			else if (DateSortDirection != SortOrder.None)
			{
				switch (DateSortDirection)
				{
					case SortOrder.Increasing:
						Transactions = new ObservableCollection<TransactionViewModel>(_transactions.OrderBy(t => t.DateTime));
						break;

					case SortOrder.Decreasing:
						Transactions = new ObservableCollection<TransactionViewModel>(_transactions.OrderByDescending(t => t.DateTime));
						break;
				}
			}
			else if (LabelSortDirection != SortOrder.None)
			{
				switch (LabelSortDirection)
				{
					case SortOrder.Increasing:
						Transactions = new ObservableCollection<TransactionViewModel>(_transactions.OrderBy(t => t.Label));
						break;

					case SortOrder.Decreasing:
						Transactions = new ObservableCollection<TransactionViewModel>(_transactions.OrderByDescending(t => t.Label));
						break;
				}
			}
		}
	}
}

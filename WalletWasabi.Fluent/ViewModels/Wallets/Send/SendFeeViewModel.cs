using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Exceptions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.MathNet;
using WalletWasabi.Fluent.Models;
using WalletWasabi.Fluent.ViewModels.Dialogs;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Gui.Converters;
using WalletWasabi.Logging;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Send
{
	[NavigationMetaData(
		Title = "Send",
		Caption = "",
		IconName = "wallet_action_send",
		NavBarPosition = NavBarPosition.None,
		Searchable = false,
		NavigationTarget = NavigationTarget.DialogScreen)]
	public partial class SendFeeViewModel : RoutableViewModel
	{
		private readonly Wallet _wallet;
		private readonly TransactionInfo _transactionInfo;
		[AutoNotify] private double[]? _confirmationTargetValues;
		[AutoNotify] private double[]? _satoshiPerByteValues;
		[AutoNotify] private string[]? _confirmationTargetLabels;
		[AutoNotify] private double _currentConfirmationTarget;
		[AutoNotify(SetterModifier = AccessModifier.Private)] private int _sliderMinimum;
		[AutoNotify(SetterModifier = AccessModifier.Private)] private int _sliderMaximum;
		[AutoNotify] private int _sliderValue;
		private bool _updatingCurrentValue;
		private FeeRate _feeRate;
		private double _lastConfirmationTarget;

		public SendFeeViewModel(Wallet wallet, TransactionInfo transactionInfo)
		{
			_wallet = wallet;
			_transactionInfo = transactionInfo;

			_sliderMinimum = 0;
			_sliderMaximum = 9;
			_currentConfirmationTarget = 36;
			_lastConfirmationTarget = _currentConfirmationTarget;
			_feeRate = new FeeRate(GetSatoshiPerByte(_lastConfirmationTarget));

			this.WhenAnyValue(x => x.CurrentConfirmationTarget)
				.Subscribe(x =>
				{
					if (x > 0)
					{
						_feeRate = new FeeRate(GetSatoshiPerByte(x));
						SetSliderValue(x);
					}
				});

			this.WhenAnyValue(x => x.SliderValue)
				.Subscribe(SetXAxisCurrentValue);

			NextCommand = ReactiveCommand.CreateFromTask(async () =>
			{
				_lastConfirmationTarget = CurrentConfirmationTarget;
				_transactionInfo.ConfirmationTimeSpan = CalculateConfirmationTime(_lastConfirmationTarget);

				await OnNextAsync();
			});
		}

		private async Task OnNextAsync()
		{
			var transactionInfo = _transactionInfo;
			var targetAnonymitySet = _wallet.ServiceConfiguration.GetMixUntilAnonymitySetValue();
			var mixedCoins = _wallet.Coins.Where(x => x.HdPubKey.AnonymitySet >= targetAnonymitySet).ToList();
			var totalMixedCoinsAmount = Money.FromUnit(mixedCoins.Sum(coin => coin.Amount), MoneyUnit.Satoshi);
			transactionInfo.Coins = mixedCoins;

			transactionInfo.FeeRate = _feeRate;

			if (transactionInfo.Amount > totalMixedCoinsAmount)
			{
				Navigate().To(new PrivacyControlViewModel(_wallet, transactionInfo));
				return;
			}

			try
			{
				if (_transactionInfo.PayJoinClient is { })
				{
					await BuildTransactionAsPayJoinAsync(transactionInfo);
				}
				else
				{
					await BuildTransactionAsNormalAsync(transactionInfo, totalMixedCoinsAmount);
				}
			}
			catch (Exception ex)
			{
				Logger.LogError(ex);
				await ShowErrorAsync("Transaction Building", ex.ToUserFriendlyString(),
					"Wasabi was unable to create your transaction.");
			}
		}

		protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
		{
			base.OnNavigatedTo(isInHistory, disposables);

			XAxisCurrentValue = _lastXAxisCurrentValue;

			var feeProvider = _wallet.FeeProvider;
			Observable
				.FromEventPattern(feeProvider, nameof(feeProvider.AllFeeEstimateChanged))
				.Select(x => (x.EventArgs as AllFeeEstimate)!.Estimations)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(estimations =>
				{
					UpdateFeeEstimates(_wallet.Network == Network.TestNet ? TestNetFeeEstimates : estimations);
				})
				.DisposeWith(disposables);

			if (feeProvider.AllFeeEstimate is { })
			{
				UpdateFeeEstimates(_wallet.Network == Network.TestNet ? TestNetFeeEstimates : feeProvider.AllFeeEstimate.Estimations);
			}
		}

		private async Task BuildTransactionAsNormalAsync(TransactionInfo transactionInfo, Money totalMixedCoinsAmount)
		{
			try
			{
				var txRes = await Task.Run(() => TransactionHelpers.BuildTransaction(_wallet, transactionInfo));
				Navigate().To(new OptimisePrivacyViewModel(_wallet, transactionInfo, txRes));
			}
			catch (InsufficientBalanceException)
			{
				var txRes = await Task.Run(() => TransactionHelpers.BuildTransaction(_wallet, transactionInfo.Address,
					totalMixedCoinsAmount, transactionInfo.Labels, transactionInfo.FeeRate, transactionInfo.Coins,
					subtractFee: true));
				var dialog = new InsufficientBalanceDialogViewModel(BalanceType.Private, txRes,
					_wallet.Synchronizer.UsdExchangeRate);
				var result = await NavigateDialogAsync(dialog, NavigationTarget.DialogScreen);

				if (result.Result)
				{
					Navigate().To(new OptimisePrivacyViewModel(_wallet, transactionInfo, txRes));
				}
				else
				{
					Navigate().To(new PrivacyControlViewModel(_wallet, transactionInfo));
				}
			}
		}

		private async Task BuildTransactionAsPayJoinAsync(TransactionInfo transactionInfo)
		{
			try
			{
				// Do not add the PayJoin client yet, it will be added before broadcasting.
				var txRes = await Task.Run(() => TransactionHelpers.BuildTransaction(_wallet, transactionInfo));
				Navigate().To(new TransactionPreviewViewModel(_wallet, transactionInfo, txRes));
			}
			catch (InsufficientBalanceException)
			{
				await ShowErrorAsync("Transaction Building",
					"There are not enough private funds to cover the transaction fee",
					"Wasabi was unable to create your transaction.");
				Navigate().To(new PrivacyControlViewModel(_wallet, transactionInfo));
			}
		}

		private TimeSpan CalculateConfirmationTime(double targetBlock)
		{
			var timeInMinutes = Math.Ceiling(targetBlock) * 10;
			var time = TimeSpan.FromMinutes(timeInMinutes);
			return time;
		}

		private void SetSliderValue(double confirmationTarget)
		{
			if (!_updatingCurrentValue)
			{
				_updatingCurrentValue = true;
				if (_confirmationTargetValues is not null)
				{
					SliderValue = GetSliderValue(confirmationTarget, _confirmationTargetValues);
				}

				_updatingCurrentValue = false;
			}
		}

		private void SetXAxisCurrentValue(int sliderValue)
		{
			if (_confirmationTargetValues is not null)
			{
				if (!_updatingCurrentValue)
				{
					_updatingCurrentValue = true;
					var index = _confirmationTargetValues.Length - sliderValue - 1;
					CurrentConfirmationTarget = _confirmationTargetValues[index];
					_updatingCurrentValue = false;
				}
			}
		}

		private void UpdateFeeEstimates(Dictionary<int, int> feeEstimates)
		{
			var xs = feeEstimates.Select(x => (double)x.Key).ToArray();
			var ys = feeEstimates.Select(x => (double)x.Value).ToArray();
#if true
			GetSmoothValuesSubdivide(xs, ys, out var xts, out var yts);
			var confirmationTargetValues = xts.ToArray();
			var satoshiPerByteValues = yts.ToArray();
#else
			var confirmationTargetValues = xs.Reverse().ToArray();
			var satoshiPerByteValues = ys.Reverse().ToArray();
#endif
			var confirmationTargetLabels = feeEstimates.Select(x => x.Key)
				.Select(x => FeeTargetTimeConverter.Convert(x, "m", "h", "h", "d", "d"))
				.Reverse()
				.ToArray();

			_updatingCurrentValue = true;
			ConfirmationTargetLabels = confirmationTargetLabels;
			ConfirmationTargetValues = confirmationTargetValues;
			SatoshiPerByteValues = satoshiPerByteValues;
			SliderMinimum = 0;
			SliderMaximum = confirmationTargetValues.Length - 1;
			CurrentConfirmationTarget = Math.Clamp(CurrentConfirmationTarget, ConfirmationTargetValues.Min(), ConfirmationTargetValues.Max());
			SliderValue = GetSliderValue(CurrentConfirmationTarget, ConfirmationTargetValues);
			_updatingCurrentValue = false;
		}

		private static readonly Dictionary<int, int> TestNetFeeEstimates = new ()
		{
			[1] = 185,
			[2] = 123,
			[3] = 123,
			[6] = 102,
			[18] = 97,
			[36] = 57,
			[72] = 22,
			[144] = 7,
			[432] = 4,
			[1008] = 4
		};

		private int GetSliderValue(double x, double[] xs)
		{
			for (var i = 0; i < xs.Length; i++)
			{
				if (xs[i] <= x)
				{
					var index = xs.Length - i - 1;
					return index;
				}
			}

			return 0;
		}

		private void GetSmoothValuesSubdivide(double[] xs, double[] ys, out List<double> xts, out List<double> yts)
		{
			const int Divisions = 256;

			xts = new List<double>();
			yts = new List<double>();

			if (xs.Length > 2)
			{
				var spline = CubicSpline.InterpolatePchipSorted(xs, ys);

				for (var i = 0; i < xs.Length - 1; i++)
				{
					var a = xs[i];
					var b = xs[i + 1];
					var range = b - a;
					var step = range / Divisions;

					var x0 = xs[i];
					xts.Add(x0);
					var yt0 = spline.Interpolate(xs[i]);
					yts.Add(yt0);

					for (var xt = a + step; xt < b; xt += step)
					{
						var yt = spline.Interpolate(xt);
						xts.Add(xt);
						yts.Add(yt);
					}
				}

				var xn = xs[^1];
				xts.Add(xn);
				var yn = spline.Interpolate(xs[^1]);
				yts.Add(yn);
			}
			else
			{
				for (var i = 0; i < xs.Length; i++)
				{
					xts.Add(xs[i]);
					yts.Add(ys[i]);
				}
			}

			xts.Reverse();
			yts.Reverse();
		}

		private decimal GetSatoshiPerByte(double t)
		{
			if (_confirmationTargetValues is { } && _satoshiPerByteValues is { })
			{
				var xs = _confirmationTargetValues.Reverse().ToArray();
				var ys = _satoshiPerByteValues.Reverse().ToArray();

				if (xs.Length > 2)
				{
					var spline = CubicSpline.InterpolatePchipSorted(xs, ys);
					var interpolated = (decimal) spline.Interpolate(t);
					return Math.Clamp(interpolated, (decimal) ys[^1], (decimal) ys[0]);
				}

				if (xs.Length == 2)
				{
					if (xs[1] - xs[0] == 0.0)
					{
						return (decimal) ys[0];
					}
					var slope = (ys[1] - ys[0]) / (xs[1] - xs[0]);
					var interpolated = (decimal)(ys[0] + (t - xs[0]) * slope);
					return Math.Clamp(interpolated, (decimal) ys[^1], (decimal) ys[0]);
				}

				if (xs.Length == 1)
				{
					return (decimal)ys[0];
				}
			}

			return SliderMaximum;
		}
	}
}

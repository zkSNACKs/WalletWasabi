using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Fluent.Models;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Home.Tiles
{
	public partial class PrivacyControlTileViewModel : TileViewModel
	{
		private readonly IObservable<Unit> _balanceChanged;
		private readonly Wallet _wallet;
		private readonly DispatcherTimer _animationTimer;
		[AutoNotify] private bool _isAutoCoinJoinEnabled;
		[AutoNotify] private bool _isBoosting;
		[AutoNotify] private bool _showBoostingAnimation;
		[AutoNotify] private bool _boostButtonVisible;
		[AutoNotify] private IList<(string color, double percentShare)>? _testDataPoints;
		[AutoNotify] private IList<DataLegend>? _testDataPointsLegend;
		[AutoNotify] private string _percentText;
		[AutoNotify] private double _percent;

		public PrivacyControlTileViewModel(WalletViewModel walletVm, IObservable<Unit> balanceChanged)
		{
			_wallet = walletVm.Wallet;
			_balanceChanged = balanceChanged;

			_animationTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(30)
			};

			DispatcherTimer.Run(() =>
			{
				Update();
				return true;
			}, TimeSpan.FromSeconds(1));

			_animationTimer.Tick += (sender, args) =>
			{
				ShowBoostingAnimation = !ShowBoostingAnimation;

				if (!ShowBoostingAnimation)
				{
					Update();
				}

				_animationTimer.Interval = ShowBoostingAnimation ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
			};

			walletVm.Settings.WhenAnyValue(x => x.AutoCoinJoin).Subscribe(x => IsAutoCoinJoinEnabled = x);

			walletVm.WhenAnyValue(x => x.IsCoinJoining)
				.Subscribe(x =>
				{
					if (x)
					{
						StartBoostAnimation();
					}
					else
					{
						StopBoostAnimation();
					}
				});

			this.WhenAnyValue(x => x.IsAutoCoinJoinEnabled, x => x.IsBoosting)
				.Subscribe(x =>
				{
					var (autoCjEnabled, isBoosting) = x;

					BoostButtonVisible = !autoCjEnabled && !isBoosting && CanCoinJoin();

					if (autoCjEnabled && isBoosting)
					{
						IsBoosting = false;
					}
				});

			BoostPrivacyCommand = ReactiveCommand.Create(() =>
			{
				var isBoosting = IsBoosting = _wallet.AllowManualCoinJoin = !IsBoosting;

				if (isBoosting)
				{
					StartBoostAnimation();
				}
				else
				{
					StopBoostAnimation();
				}
			});
		}

		public ICommand BoostPrivacyCommand { get; }

		private bool CanCoinJoin()
		{
			var privateThreshold = _wallet.ServiceConfiguration.GetMixUntilAnonymitySetValue();

			return _wallet.Coins.Any(x => x.HdPubKey.AnonymitySet < privateThreshold);
		}

		private void StartBoostAnimation()
		{
			ShowBoostingAnimation = true;
			_animationTimer.Interval = TimeSpan.FromSeconds(5);
			_animationTimer.IsEnabled = true;
		}

		private void StopBoostAnimation()
		{
			ShowBoostingAnimation = false;
			_animationTimer.IsEnabled = false;
		}

		protected override void OnActivated(CompositeDisposable disposables)
		{
			base.OnActivated(disposables);

			_balanceChanged
				.Subscribe(_ => Update())
				.DisposeWith(disposables);
		}

		private void Update()
		{
			var privateThreshold = _wallet.ServiceConfiguration.GetMixUntilAnonymitySetValue();

			var privateAmount = _wallet.Coins.FilterBy(x => x.HdPubKey.AnonymitySet >= privateThreshold).TotalAmount();
			var unmixed = _wallet.Coins.FilterBy(x => x.HdPubKey.AnonymitySet == 0).TotalAmount();
			var partialyMixed = _wallet.Coins.FilterBy(x =>
				x.HdPubKey.AnonymitySet > 0 && x.HdPubKey.AnonymitySet < privateThreshold).TotalAmount();

			var queued = _wallet.Coins.FilterBy(x => x.CoinJoinInProgress).TotalAmount().ToDecimal(MoneyUnit.BTC);

			var privateDecimalAmount = privateAmount.ToDecimal(MoneyUnit.BTC);
			var partiallyPrivate = partialyMixed.ToDecimal(MoneyUnit.BTC);
			var normalDecimalAmount = unmixed.ToDecimal(MoneyUnit.BTC);
			var totalDecimalAmount = _wallet.Coins.TotalAmount().ToDecimal(MoneyUnit.BTC);

			var pcQueued = totalDecimalAmount == 0M ? 0d : (double)(queued / totalDecimalAmount);
			var pcPrivate = totalDecimalAmount == 0M ? 0d : (double)(privateDecimalAmount / totalDecimalAmount);
			var pcNormal = totalDecimalAmount == 0M ? 0d : (double)(normalDecimalAmount / totalDecimalAmount);
			var pcPartial = totalDecimalAmount == 0M ? 0d : (double)((partiallyPrivate - queued) / totalDecimalAmount);

			Console.WriteLine($"queued: {pcQueued}, private: {pcPrivate}, normal: {pcNormal}, partial: {pcPartial}");

			PercentText = $"{pcPrivate:P}";

			Percent = pcPrivate * 100;

			TestDataPoints = new List<(string, double)>
			{
				("#78A827", pcPrivate),
				("#7F00A2E8", pcQueued),
				("#3FDDA73E", pcPartial)
			};

			TestDataPointsLegend = new List<DataLegend>
			{
				new(privateAmount, "Private", "#78A827", pcPrivate),
				new(unmixed, "Not Private", "#D8DED7", pcNormal)
			};
		}
	}
}

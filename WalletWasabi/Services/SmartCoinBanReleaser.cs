using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Bases;
using WalletWasabi.Wallets;

namespace WalletWasabi.Services;
public class SmartCoinBanReleaser : PeriodicRunner
{
	public SmartCoinBanReleaser(TimeSpan period, WalletManager walletManager) : base(period)
	{
		WalletManager = walletManager;
	}

	public WalletManager WalletManager { get; }

	protected override Task ActionAsync(CancellationToken cancel)
	{
		foreach (var coins in WalletManager.GetWallets(refreshWalletList: false).Select(wallet => wallet.Coins))
		{
			coins?.CheckCoinsReleaseTime();
		}

		return Task.CompletedTask;
	}
}

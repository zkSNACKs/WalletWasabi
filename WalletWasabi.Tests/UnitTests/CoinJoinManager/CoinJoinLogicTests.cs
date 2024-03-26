using Xunit;
using WalletWasabi.WabiSabi.Client.CoinJoin.Manager.Logic;
using WalletWasabi.WabiSabi.Client.StatusChangedEvents;
using System.Threading.Tasks;

namespace WalletWasabi.Tests.UnitTests.CoinJoinManager;

public class CoinJoinLogicTests
{
	[Fact]
	public async Task CanStartCoinJoinTestAsync()
	{
		// If the UI is blocking we should not mix. No matter what.
		Assert.Equal(
			CoinjoinError.UserInSendWorkflow,
			await CoinJoinLogic.CheckWalletStartCoinJoinAsync(
					wallet: new MockWallet(
						isUnderPlebStop: false,
						isWalletPrivate: false),
					walletBlockedByUi: true,
					overridePlebStop: true));

		// We mix if Payments are batched, even if the wallet is private.
		Assert.Equal(
			CoinjoinError.None,
			await CoinJoinLogic.CheckWalletStartCoinJoinAsync(
					wallet: new MockWallet(
						isUnderPlebStop: true,
						isWalletPrivate: true,
						addBatchedPayment: true),
					walletBlockedByUi: false,
					overridePlebStop: false));

		// If all private we won't mix.
		Assert.Equal(
			CoinjoinError.AllCoinsPrivate,
			await CoinJoinLogic.CheckWalletStartCoinJoinAsync(
					wallet: new MockWallet(
						isUnderPlebStop: false,
						isWalletPrivate: true),
					walletBlockedByUi: false,
					overridePlebStop: false));

		// Plebstop is not overwritten.
		Assert.Equal(
			CoinjoinError.NotEnoughUnprivateBalance,
			await CoinJoinLogic.CheckWalletStartCoinJoinAsync(
					wallet: new MockWallet(
						isUnderPlebStop: true,
						isWalletPrivate: false),
					walletBlockedByUi: false,
					overridePlebStop: false));

		// Plebstop is overwritten.
		Assert.Equal(
			CoinjoinError.None,
			await CoinJoinLogic.CheckWalletStartCoinJoinAsync(
					wallet: new MockWallet(
						isUnderPlebStop: true,
						isWalletPrivate: false),
					walletBlockedByUi: false,
					overridePlebStop: true));
	}
}

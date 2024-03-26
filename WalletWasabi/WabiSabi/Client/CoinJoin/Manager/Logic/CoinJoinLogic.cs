using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Helpers;
using WalletWasabi.WabiSabi.Client.Banning;
using WalletWasabi.WabiSabi.Client.CoinJoin.Client;
using WalletWasabi.WabiSabi.Client.StatusChangedEvents;
using WalletWasabi.Wallets;

namespace WalletWasabi.WabiSabi.Client.CoinJoin.Manager.Logic;

public static class CoinJoinLogic
{
	public static async Task<CoinjoinError> CheckWalletStartCoinJoinAsync(IWallet wallet, bool walletBlockedByUi, bool overridePlebStop)
	{
		// CoinJoin blocked by the UI. User is in action.
		if (walletBlockedByUi)
		{
			return CoinjoinError.UserInSendWorkflow;
		}

		// If payments are batched we always mix.
		if (wallet.BatchedPayments.AreTherePendingPayments)
		{
			return CoinjoinError.None;
		}

		// The wallet is already private.
		if (await wallet.IsWalletPrivateAsync().ConfigureAwait(false))
		{
			return CoinjoinError.AllCoinsPrivate;
		}

		// Wallet total balance is lower then the PlebStop threshold. If the user not overrides that, we won't mix.
		if (!overridePlebStop && wallet.IsUnderPlebStop)
		{
			return CoinjoinError.NotEnoughUnprivateBalance;
		}

		return CoinjoinError.None;
	}

	public static CoinjoinError CheckCoinsStartCoinJoin(IEnumerable<SmartCoin> walletCoins, int bestHeight, int anonScoreTarget, CoinRefrigerator coinRefrigerator, CoinPrison coinPrison, out SmartCoin[] smartCoins)
	{
		smartCoins = [];
		SmartCoin[] coinCandidates = new CoinsView(walletCoins)
				.Available() // SpenderTransaction is null && !SpentAccordingToBackend && !CoinJoinInProgress;
				.ToArray();

		// If there is no available coin candidates, then don't mix.
		if (coinCandidates.Length == 0)
		{
			return CoinjoinError.NoCoinsEligibleToMix;
		}

		var frozenCoins = coinCandidates.Where(x => coinRefrigerator.IsFrozen(x)).ToArray();
		var bannedCoins = coinCandidates.Where(x => coinPrison.TryGetOrRemoveBannedCoin(x, out _)).ToArray();
		var immatureCoins = coinCandidates.Where(x => x.Transaction.IsImmature(bestHeight)).ToArray();
		var unconfirmedCoins = coinCandidates.Where(x => !x.Confirmed).ToArray();
		var excludedCoins = coinCandidates.Where(x => x.IsExcludedFromCoinJoin).ToArray();

		coinCandidates = coinCandidates
			.Except(frozenCoins)
			.Except(bannedCoins)
			.Except(immatureCoins)
			.Except(unconfirmedCoins)
			.Except(excludedCoins)
			.ToArray();

		if (coinCandidates.Length == 0)
		{
			var anyFrozenCoins = frozenCoins.Any(x => !x.IsPrivate(anonScoreTarget));
			var anyNonPrivateUnconfirmed = unconfirmedCoins.Any(x => !x.IsPrivate(anonScoreTarget));
			var anyNonPrivateImmature = immatureCoins.Any(x => !x.IsPrivate(anonScoreTarget));
			var anyNonPrivateBanned = bannedCoins.Any(x => !x.IsPrivate(anonScoreTarget));
			var anyNonPrivateExcluded = excludedCoins.Any(x => !x.IsPrivate(anonScoreTarget));

			if (anyFrozenCoins)
			{
				return CoinjoinError.NoCoinsEligibleToMix;
			}

			if (anyNonPrivateUnconfirmed)
			{
				return CoinjoinError.NoConfirmedCoinsEligibleToMix;
			}

			if (anyNonPrivateImmature)
			{
				return CoinjoinError.OnlyImmatureCoinsAvailable;
			}

			if (anyNonPrivateBanned)
			{
				return CoinjoinError.CoinsRejected;
			}

			if (anyNonPrivateExcluded)
			{
				return CoinjoinError.OnlyExcludedCoinsAvailable;
			}
		}

		if (coinCandidates.All(x => x.IsPrivate(anonScoreTarget)))
		{
			return CoinjoinError.NoCoinsEligibleToMix;
		}

		smartCoins = coinCandidates;

		return CoinjoinError.None;
	}
}

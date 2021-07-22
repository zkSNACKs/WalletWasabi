using System;
using System.Collections.Generic;
using System.Linq;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.Helpers
{
	public static class WalletHelpers
	{
		public static WalletType GetType(KeyManager keyManager)
		{
			if (keyManager.Icon is { } icon &&
			    Enum.TryParse(typeof(WalletType), icon, true, out var type) &&
			    type is { })
			{
				return (WalletType) type;
			}

			return keyManager.IsHardwareWallet ? WalletType.Hardware : WalletType.Normal;
		}

		public static IEnumerable<string> GetLabels()
		{
			// Don't refresh wallet list as it may be slow.
			IEnumerable<SmartLabel> labels = Services.WalletManager.GetWallets(refreshWalletList: false)
				.Select(x => x.KeyManager)
				.SelectMany(x => x.GetLabels());

			var txStore = Services.BitcoinStore.TransactionStore;
			if (txStore is { })
			{
				labels = labels.Concat(txStore.GetLabels());
			}

			return labels.SelectMany(x => x.Labels);
		}
	}
}

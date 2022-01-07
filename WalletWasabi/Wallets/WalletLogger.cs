using NBitcoin;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Blockchain.TransactionProcessing;

namespace WalletWasabi.Wallets
{
	public class WalletLogger
	{
		public WalletLogger(string walletName, Network network, ICoinsView coins, string dataDir)
		{
			if (!Enabled)
			{
				throw new InvalidProgramException("WalletLogger is not enabled.");
			}

			WalletName = walletName;
			Network = network;
			Coins = coins;
			DataDir = dataDir;
			if (network == Network.Main)
			{
				BlockExplorerPrefix = "https://mempool.space/tx/";
			}
			else if (network == Network.TestNet)
			{
				BlockExplorerPrefix = "https://mempool.space/testnet/tx/";
			}
			else
			{
				BlockExplorerPrefix = "";
			}

			LastBalance = Coins.TotalAmount();
			LastCoinsText = CoinsToString(Coins);

			TryCreateFile();
		}

		private string WalletName { get; }
		private string BlockExplorerPrefix { get; }
		private string FilePath { get; set; }
		private Network Network { get; }
		public ICoinsView Coins { get; }
		private string DataDir { get; }
		private Money LastBalance { get; }
		private int CurrentDay { get; set; } = 0;
		public static bool Enabled { get; set; } = false;

		public string LastCoinsText { get; set; } = "";
		private Dictionary<SmartCoin, int> LastCoinAnonSets { get; set; } = new();

		public async Task LogAsync(ProcessedResult e)
		{
			TryCreateFile();

			var currentBalance = Coins.TotalAmount();
			StringBuilder sb = new();

			if (e.NewlySpentCoins.Any())
			{
				sb.AppendLine($"Spent");
				foreach (var coin in e.NewlySpentCoins.OrderByDescending(c => c.Amount))
				{
					sb.AppendLine(CoinToString(coin, LastCoinAnonSets));
				}
			}

			if (e.NewlyReceivedCoins.Any())
			{
				sb.AppendLine($"Received");
				foreach (var coin in e.NewlyReceivedCoins.OrderByDescending(c => c.Amount))
				{
					sb.AppendLine(CoinToString(coin));
				}
			}

			if (e.ReceivedDusts.Any())
			{
				sb.AppendLine($"Received dust");
				foreach (var coin in e.ReceivedDusts.OrderByDescending(c => c.Value))
				{
					sb.AppendLine($"{coin.Value}");
				}
			}

			if (e.ReplacedCoins.Any() || e.RestoredCoins.Any() || e.SuccessfullyDoubleSpentCoins.Any())
			{
				sb.AppendLine($"ReplacedCoins ({e.ReplacedCoins.Count})");
				sb.AppendLine($"RestoredCoins ({e.RestoredCoins.Count})");
				sb.AppendLine($"SuccessfullyDoubleSpentCoins ({e.SuccessfullyDoubleSpentCoins.Count})");
			}

			if (sb.Length > 0)
			{
				StringBuilder header = new();
				header.AppendLine();
				header.AppendLine($"Balance change: { currentBalance - LastBalance } - Total balance: {Coins.TotalAmount()}");
				header.AppendLine($"TxId: {BlockExplorerPrefix}{e.Transaction.GetHash()} {DateTime.Now}");
				sb.Insert(0, header.ToString());

				if (Coins.Any())
				{
					sb.AppendLine();
					sb.AppendLine($"Prev {LastCoinsText}");
					var text = CoinsToString(Coins);
					sb.AppendLine($"Curr {text}");

					LastCoinsText = text;
					LastCoinAnonSets = Coins.ToDictionary(c => c, c => c.HdPubKey.AnonymitySet);
				}
			}

			await File.AppendAllTextAsync(FilePath, sb.ToString()).ConfigureAwait(false);
		}

		[MemberNotNull(nameof(FilePath))]
		private void TryCreateFile()
		{
			if (CurrentDay != DateTime.Now.Day)
			{
				CurrentDay = DateTime.Now.Day;
				FilePath = Path.Combine(DataDir, "WalletLogs", WalletName, $"WalletLog_{WalletName}_{DateTime.Now:yyyyMMdd}.txt");
			}

			if (FilePath is null)
			{
				throw new ArgumentNullException(nameof(FilePath));
			}

			if (File.Exists(FilePath))
			{
				return;
			}

			IoHelpers.EnsureContainingDirectoryExists(FilePath);

			StringBuilder sb = new();

			sb.AppendLine($"WARNING! This file created only for debugging purposes! In any other cases immediately disable WalletLogging feature!");
			sb.AppendLine($"WalletName: {WalletName} File created: {DateTime.Now}");

			sb.AppendLine($"Total balance: {Coins.TotalAmount()}");
			if (Coins.Any())
			{
				sb.AppendLine($"Coins ({Coins.Count():0##}):");
				foreach (var coin in Coins.OrderByDescending(c => c.Amount))
				{
					sb.AppendLine(CoinToString(coin));
				}
			}

			File.WriteAllText(FilePath, sb.ToString());
		}

		private static string CoinToString(SmartCoin coin, Dictionary<SmartCoin, int>? lastCoinAnonSets = null)
		{
			var anon = coin.HdPubKey.AnonymitySet;
			if (lastCoinAnonSets?.ContainsKey(coin) is true)
			{
				anon = lastCoinAnonSets[coin];
			}
			return $"{coin.Amount}({anon:0##})@{coin.OutPoint.Hash.ToString()[..5]}{coin.OutPoint.N:0#}";
		}

		private static string CoinsToString(IEnumerable<SmartCoin> coins)
		{
			var coinsText = string.Join(" ", coins.OrderByDescending(c => c.Amount).Select(coin => CoinToString(coin)));
			var currentCoinsText = $"[{coins.Count():0##}] {coinsText}";
			return currentCoinsText;
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.Wallets;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Integration
{
	internal class Participant
	{
		public Participant(IRPCClient rpc, WabiSabiHttpApiClient apiClient)
		{
			Rpc = rpc;
			ApiClient = apiClient;

			KeyManager = KeyManager.CreateNew(out var _, password: "");
			KeyManager.AssertCleanKeysIndexed();
		}

		public KeyManager KeyManager { get; }
		public List<Coin> Coins { get; } = new();
		public IRPCClient Rpc { get; }
		public WabiSabiHttpApiClient ApiClient { get; }

		private Coin? SourceCoin { get; set; }

		public async Task GenerateSourceCoinAsync(CancellationToken cancellationToken)
		{
			var minerKey = KeyManager.GetNextReceiveKey("coinbase", out _);
			var blockIds = await Rpc.GenerateToAddressAsync(1, minerKey.GetP2wpkhAddress(Rpc.Network)).ConfigureAwait(false);
			var block = await Rpc.GetBlockAsync(blockIds.First()).ConfigureAwait(false);
			SourceCoin = block.Transactions[0].Outputs.GetCoins(minerKey.P2wpkhScript).First();
			minerKey.SetKeyState(KeyState.Used);
		}

		public async Task GenerateCoinsAsync(int numberOfCoins, CancellationToken cancellationToken)
		{
			var feeRate = new FeeRate(4.0m);
			var splitTx = Transaction.Create(Rpc.Network);
			splitTx.Inputs.Add(new TxIn(SourceCoin!.Outpoint));

			var rnd = new Random();
			var sampling = Enumerable
				.Range(0, numberOfCoins-1)
				.Select(_ => rnd.NextDouble())
				.Prepend(0)
				.Prepend(1)
				.OrderBy(x => x)
				.ToArray();

			var amounts = sampling
				.Zip(sampling.Skip(1), (x, y) => y - x)
				.Select(x => x * SourceCoin.Amount.Satoshi)
				.Select(x => Money.Satoshis((long)x));

			foreach (var amount in amounts)
			{
				var key = KeyManager.GetNextReceiveKey("no-label", out _);
				var scriptPubKey = key.P2wpkhScript;
				var effectiveOutputValue = amount - feeRate.GetFee(scriptPubKey.EstimateOutputVsize());
				splitTx.Outputs.Add(new TxOut(effectiveOutputValue, scriptPubKey));
			}
			var minerKey = KeyManager.GetSecrets("", SourceCoin.ScriptPubKey).First();
			splitTx.Sign(minerKey.PrivateKey.GetBitcoinSecret(Rpc.Network), SourceCoin);
			Coins.AddRange(splitTx.Outputs.AsCoins());
			await Rpc.SendRawTransactionAsync(splitTx).ConfigureAwait(false);
		}

		public async Task StartParticipatingAsync(CancellationToken cancellationToken)
		{
			using var roundStateUpdater = new RoundStateUpdater(TimeSpan.FromSeconds(3), ApiClient);
			await roundStateUpdater.StartAsync(cancellationToken).ConfigureAwait(false);

			var kitchen = new Kitchen();
			kitchen.Cook("");

			var coinJoinClient = new CoinJoinClient(ApiClient, Coins, kitchen, KeyManager, roundStateUpdater);

			// Run the coinjoin client task.
			await coinJoinClient.StartCoinJoinAsync(cancellationToken).ConfigureAwait(false);

			await roundStateUpdater.StopAsync(cancellationToken).ConfigureAwait(false);
		}
	}
}

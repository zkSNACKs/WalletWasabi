using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Protocol;
using WabiSabi.Crypto.Randomness;
using WalletWasabi.Extensions;
using WalletWasabi.Helpers;
using WalletWasabi.Wallets.BlockProvider;

namespace WalletWasabi.Wallets;

public class P2PNodesManager
{
	public P2PNodesManager(Network network, NodesGroup nodes, bool isTorEnabled)
	{
		Network = network;
		Nodes = nodes;
		IsTorEnabled = isTorEnabled;
	}

	private Network Network { get; }
	private NodesGroup Nodes { get; }
	private bool IsTorEnabled { get; }
	public uint ConnectedNodesCount => (uint)Nodes.ConnectedNodes.Count;

	public static double SuggestedTimeout => RuntimeParams.Instance.NetworkNodeTimeout;

	private Dictionary<EndPoint, NodeMetadata> NodesUsageHistory { get; } = new ();
	private Queue<TimeSpan> LastTenSuccessesDurations { get; } = new (10);
	private Queue<TimeSpan> LastTenCancellationDurations { get; } = new (10);

	private record NodeMetadata()
	{
		public Dictionary<P2pSourceDataStatusCode, List<TimeSpan>> Durations { get; } = new();
		public int Counter => Durations.Sum(x => x.Value.Count);
		public P2pSourceDataStatusCode? HighestOffense { get; set; }
		public int? Score { get; set; } = null;
	}

	public async Task<Node?> GetBestNodeAsync(CancellationToken cancellationToken)
	{
		while (Nodes.ConnectedNodes.Count == 0)
		{
			await Task.Delay(100, cancellationToken).ConfigureAwait(false);
		}

		// Select a random node we are connected to.
		return Nodes.ConnectedNodes.RandomElement(InsecureRandom.Instance);
	}

	public async Task NotifyDownloadFinishedAsync(P2pSourceData p2PSourceData)
	{
		if (p2PSourceData.Node is null || p2PSourceData.StatusCode == P2pSourceDataStatusCode.NoPeerAvailable)
		{
			return;
		}

		if (!NodesUsageHistory.TryGetValue(p2PSourceData.Node.RemoteSocketEndpoint, out var nodeMetadata))
		{
			nodeMetadata = new NodeMetadata();
			NodesUsageHistory[p2PSourceData.Node.RemoteSocketEndpoint] = nodeMetadata;
		}

		if (!nodeMetadata.Durations.TryGetValue(p2PSourceData.StatusCode, out var durationsForStatusCode))
		{
			durationsForStatusCode = new List<TimeSpan>();
			nodeMetadata.Durations[p2PSourceData.StatusCode] = durationsForStatusCode;
		}

		durationsForStatusCode.Add(p2PSourceData.Duration);

		if (p2PSourceData.StatusCode == P2pSourceDataStatusCode.Success)
		{
			if (LastTenSuccessesDurations.Count == 10)
			{
				LastTenSuccessesDurations.Dequeue();
			}
			LastTenSuccessesDurations.Enqueue(p2PSourceData.Duration);
		}
		else
		{
			if (p2PSourceData.StatusCode == P2pSourceDataStatusCode.Cancelled)
			{
				if (LastTenCancellationDurations.Count == 10)
				{
					LastTenCancellationDurations.Dequeue();
				}

				LastTenCancellationDurations.Enqueue(p2PSourceData.Duration);
			}

			if(nodeMetadata.HighestOffense is null || p2PSourceData.StatusCode > nodeMetadata.HighestOffense)
			{
				nodeMetadata.HighestOffense = p2PSourceData.StatusCode;
			}
		}

		await UpdateSuggestedTimeoutAsync().ConfigureAwait(false);

		// TODO: Give a score to the node, it can be relative to the quality of the other nodes that we have
		// TODO: Then disconnect the node if its score is too low.
		ComputeNodeScore(nodeMetadata);
		DisconnectNodeIfScoreTooLow(nodeMetadata);
	}

	private async Task UpdateSuggestedTimeoutAsync()
	{
		if (LastTenSuccessesDurations.Count + LastTenCancellationDurations.Count == 0)
		{
			return;
		}

		var avgSuccesses = ComputeAverageWithoutExtremeValues(LastTenSuccessesDurations.Select(x => x.TotalSeconds).ToList());
		var avgCancellations = ComputeAverageWithoutExtremeValues(LastTenCancellationDurations.Select(x => x.TotalSeconds).ToList());

		// TODO: Use the last avgSuccesses and last avgCancellations to figure out a great value for new timeout
		// TODO: This is a global timeout, but we could also give specific timeout for specific nodes
		var newSuggestedTimeout = SuggestedTimeout;

		RuntimeParams.Instance.NetworkNodeTimeout = (int) newSuggestedTimeout;
		await RuntimeParams.Instance.SaveAsync().ConfigureAwait(false);
	}

	private static double ComputeAverageWithoutExtremeValues(IReadOnlyCollection<double> values, double percentToIgnoreOnBothSides = 0.2)
	{
		var toIgnoreFromBothSides = (int)Math.Round(percentToIgnoreOnBothSides * values.Count);
		return values.Order().Skip(toIgnoreFromBothSides).Take(values.Count - (toIgnoreFromBothSides * 2)).Average(x => x);
	}
}

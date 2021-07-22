using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Crypto.ZeroKnowledge;
using WalletWasabi.WabiSabi.Client.CredentialDependencies;
using WalletWasabi.WabiSabi.Crypto;

namespace WalletWasabi.WabiSabi.Client
{
	public class DependencyGraphTaskScheduler
	{
		public DependencyGraphTaskScheduler(DependencyGraph graph)
		{
			Graph = graph;
			var allInEdges = Enum.GetValues<CredentialType>()
				.SelectMany(type => Enumerable.Concat<RequestNode>(Graph.Reissuances, Graph.Outputs)
				.SelectMany(node => Graph.EdgeSets[type].InEdges(node)));
			DependencyTasks = allInEdges.ToDictionary(edge => edge, _ => new TaskCompletionSource<Credential>(TaskCreationOptions.RunContinuationsAsynchronously));
		}

		private DependencyGraph Graph { get; }
		private Dictionary<CredentialDependency, TaskCompletionSource<Credential>> DependencyTasks { get; }

		public async Task StartConfirmConnectionsAsync(IEnumerable<AliceClient> aliceClients, DependencyGraph dependencyGraph, TimeSpan connectionConfirmationTimeout, CancellationToken cancellationToken)
		{
			var aliceNodePairs = PairAliceClientAndRequestNodes(aliceClients, Graph);

			List<SmartRequestNode> smartRequestNodes = new();
			List<Task> connectionConfirmationTasks = new();

			using CancellationTokenSource ctsOnError = new();
			using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ctsOnError.Token);

			foreach ((var aliceClient, var node) in aliceNodePairs)
			{
				var amountEdgeTaskCompSources = Graph.OutEdges(node, CredentialType.Amount).Select(edge => DependencyTasks[edge]);
				var vsizeEdgeTaskCompSources = Graph.OutEdges(node, CredentialType.Vsize).Select(edge => DependencyTasks[edge]);
				SmartRequestNode smartRequestNode = new(
					Enumerable.Empty<Task<Credential>>(),
					Enumerable.Empty<Task<Credential>>(),
					amountEdgeTaskCompSources,
					vsizeEdgeTaskCompSources);

				var amountsToRequest = dependencyGraph.OutEdges(node, CredentialType.Amount).Select(e => (long)e.Value);
				var vsizesToRequest = dependencyGraph.OutEdges(node, CredentialType.Vsize).Select(e => (long)e.Value);

				var task = smartRequestNode
					.ConfirmConnectionTaskAsync(
						connectionConfirmationTimeout,
						aliceClient,
						amountsToRequest,
						vsizesToRequest,
						node.EffectiveValue,
						node.VsizeRemainingAllocation,
						linkedCts.Token)
					.ContinueWith((t) =>
					{
						if (t.IsFaulted && t.Exception is { } exception)
						{
							// If one task is failing, cancel all the tasks and throw.
							ctsOnError.Cancel();
							throw exception;
						}
					}, linkedCts.Token);

				connectionConfirmationTasks.Add(task);
			}

			await Task.WhenAll(connectionConfirmationTasks).ConfigureAwait(false);

			var amountEdges = Graph.Inputs.SelectMany(node => Graph.OutEdges(node, CredentialType.Amount));
			var vsizeEdges = Graph.Inputs.SelectMany(node => Graph.OutEdges(node, CredentialType.Vsize));

			// Check if all tasks were finished, otherwise Task.Result will block.
			if (!amountEdges.Concat(vsizeEdges).All(edge => DependencyTasks[edge].Task.IsCompletedSuccessfully))
			{
				throw new InvalidOperationException("Some Input nodes out-edges failed to complete.");
			}
		}

		public async Task StartReissuancesAsync(IEnumerable<AliceClient> aliceClients, BobClient bobClient, CancellationToken cancellationToken)
		{
			var aliceNodePairs = PairAliceClientAndRequestNodes(aliceClients, Graph);

			// Build tasks and link them together.
			List<SmartRequestNode> smartRequestNodes = new();
			List<Task> allTasks = new();

			using CancellationTokenSource ctsOnError = new();
			using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ctsOnError.Token);

			foreach (var node in Graph.Reissuances)
			{
				var inputAmountEdgeTasks = Graph.InEdges(node, CredentialType.Amount).Select(edge => DependencyTasks[edge].Task);
				var inputVsizeEdgeTasks = Graph.InEdges(node, CredentialType.Vsize).Select(edge => DependencyTasks[edge].Task);

				var outputAmountEdgeTaskCompSources = Graph.OutEdges(node, CredentialType.Amount).Select(edge => DependencyTasks[edge]);
				var outputVsizeEdgeTaskCompSources = Graph.OutEdges(node, CredentialType.Vsize).Select(edge => DependencyTasks[edge]);

				var requestedAmounts = Graph.OutEdges(node, CredentialType.Amount).Select(edge => (long)edge.Value);
				var requestedVSizes = Graph.OutEdges(node, CredentialType.Vsize).Select(edge => (long)edge.Value);

				SmartRequestNode smartRequestNode = new(
					inputAmountEdgeTasks,
					inputVsizeEdgeTasks,
					outputAmountEdgeTaskCompSources,
					outputVsizeEdgeTaskCompSources);

				var task = smartRequestNode
					.StartReissuanceAsync(bobClient, requestedAmounts, requestedVSizes, linkedCts.Token)
					.ContinueWith((t) =>
				{
					if (t.IsFaulted && t.Exception is { } exception)
					{
						// If one task is failing, cancel all the tasks and throw.
						ctsOnError.Cancel();
						throw exception;
					}
				}, linkedCts.Token);
				allTasks.Add(task);
			}

			await Task.WhenAll(allTasks).ConfigureAwait(false);

			var amountEdges = Graph.Outputs.SelectMany(node => Graph.InEdges(node, CredentialType.Amount));
			var vsizeEdges = Graph.Outputs.SelectMany(node => Graph.InEdges(node, CredentialType.Vsize));

			// Check if all tasks were finished, otherwise Task.Result will block.
			if (!amountEdges.Concat(vsizeEdges).All(edge => DependencyTasks[edge].Task.IsCompletedSuccessfully))
			{
				throw new InvalidOperationException("Some Output nodes in-edges failed to complete");
			}
		}

		public async Task StartOutputRegistrationsAsync(IEnumerable<TxOut> txOuts, BobClient bobClient, CancellationToken cancellationToken)
		{
			List<Task> outputTasks = new();

			using CancellationTokenSource ctsOnError = new();
			using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ctsOnError.Token);

			foreach (var (node, txOut) in Enumerable.Zip(Graph.Outputs, txOuts))
			{
				var amountCredsToPresentTasks = Graph.InEdges(node, CredentialType.Amount).Select(edge => DependencyTasks[edge].Task);
				var vsizeCredsToPresentTasks = Graph.InEdges(node, CredentialType.Vsize).Select(edge => DependencyTasks[edge].Task);

				SmartRequestNode smartRequestNode = new(
					amountCredsToPresentTasks,
					vsizeCredsToPresentTasks,
					Array.Empty<TaskCompletionSource<Credential>>(),
					Array.Empty<TaskCompletionSource<Credential>>());

				var task = smartRequestNode
					.StartOutputRegistrationAsync(bobClient, txOut.ScriptPubKey, cancellationToken)
					.ContinueWith((t) =>
					{
						if (t.IsFaulted && t.Exception is { } exception)
						{
							// If one task is failing, cancel all the tasks and throw.
							ctsOnError.Cancel();
							throw exception;
						}
					}, linkedCts.Token);
				outputTasks.Add(task);
			}

			await Task.WhenAll(outputTasks).ConfigureAwait(false);
		}

		private IEnumerable<(AliceClient AliceClient, InputNode Node)> PairAliceClientAndRequestNodes(IEnumerable<AliceClient> aliceClients, DependencyGraph graph)
		{
			var inputNodes = graph.Inputs;

			if (aliceClients.Count() != inputNodes.Count)
			{
				throw new InvalidOperationException("Graph vs Alice inputs mismatch");
			}

			return Enumerable.Zip(aliceClients, inputNodes);
		}
	}
}

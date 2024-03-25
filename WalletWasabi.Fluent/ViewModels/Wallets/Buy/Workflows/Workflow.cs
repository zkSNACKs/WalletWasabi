using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using WalletWasabi.BuyAnything;
using WalletWasabi.Extensions;
using WalletWasabi.Fluent.ViewModels.Wallets.Buy.Workflows.ShopinBit;
using WalletWasabi.Logging;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Buy.Workflows;

public abstract partial class Workflow : ReactiveObject, IDisposable
{
	private readonly CompositeDisposable _disposables = new();
	[AutoNotify] private IWorkflowStep? _currentStep;
	[AutoNotify] private Conversation _conversation;
	[AutoNotify] private bool _isCompleted;
	[AutoNotify] private bool _isDeletedInSib;

	protected Workflow(Conversation conversation)
	{
		_conversation = conversation;

		BindCurrentStepConversation();

		this.WhenAnyValue(x => x.Conversation)
			.Do(x =>
			{
				if (CurrentStep is { } step)
				{
					step.Conversation = x;
				}
			})
			.Subscribe()
			.DisposeWith(_disposables);
	}

	public event EventHandler<Exception>? OnStepError;

	public abstract IMessageEditor MessageEditor { get; }

	public abstract Task ExecuteAsync(CancellationToken token);

	protected async Task ExecuteStepAsync(IWorkflowStep step, CancellationToken token)
	{
		CurrentStep = step;
		step.Conversation = Conversation;

		Exception? lastException = null;

		// this is looped until Step execution is successfully completed or cancellation is requested.
		// If it errors out, then the Workflow won't move forward to the next step.
		// All Steps should be be able to be re-executed more than once, gracefully.
		while (true)
		{
			try
			{
				await step.ExecuteAsync();
				break;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				step.Reset();

				if (step.IsInteractive)
				{
					OnStepError.SafeInvoke(this, ex);
					Logger.LogError($"An error occurred trying to execute Step '{step.GetType().Name}' in Workflow '{GetType().Name}'", ex);
				}
				else
				{
					if (lastException?.Message != ex.Message)
					{
						lastException = ex;
						OnStepError.SafeInvoke(this, ex);
						Logger.LogError($"An error occurred trying to execute Step '{step.GetType().Name}' in Workflow '{GetType().Name}'", ex);
					}

					// Keep the step Busy under the Delay so the user will not notice anything in the UI.
					var isBusy = step.IsBusy;
					step.IsBusy = true;
					await Task.Delay(3000, token);
					step.IsBusy = isBusy;
				}
			}
		}
	}

	protected void WorkflowCompleted()
	{
		CurrentStep = null;
		IsDeletedInSib = true;
	}

	/// <summary>
	/// Marks the conversation messages as read and Saves to disk.
	/// </summary>
	/// <param name="cancellationToken">The cancellation token</param>
	public async Task MarkConversationAsReadAsync(CancellationToken cancellationToken)
	{
		if (CurrentStep is { })
		{
			CurrentStep.IsBusy = true;
		}

		try
		{
			if (!Conversation.ChatMessages.Any(x => x.IsUnread))
			{
				return;
			}
			
			Conversation = Conversation.MarkAsRead();

			if (Conversation.Id == ConversationId.Empty)
			{
				return;
			}

			var buyAnythingManager = Services.HostedServices.Get<BuyAnythingManager>();

			await Task.Run(() => buyAnythingManager.UpdateConversationOnlyLocallyAsync(Conversation, cancellationToken));
		}
		finally
		{
			if (CurrentStep is { })
			{
				CurrentStep.IsBusy = false;
			}
		}
	}

	public static Workflow Create(Wallet wallet, Conversation conversation)
	{
		// If another type of workflow is required in the future this is the place where it should be defined
		var workflow = new ShopinBitWorkflow(wallet, conversation);

		return workflow;
	}

	private void BindCurrentStepConversation()
	{
#pragma warning disable CS8602
		// Dereference of a possibly null reference.
		// Reason: this warning is dubious here.
		// The parameter of WhenAnyValue() is an Expression (from System.Linq.Expressions).
		// It's not directly executable code, therefore it cannot raise a NullReferenceException
		// Also, null propagation isn't allowed by the compiler inside such an Expression,
		// so the only way to remove this warning is to make CurrentStep non-nullable, which doesn't make sense by design.
		this.WhenAnyValue(x => x.CurrentStep.Conversation)
			.BindTo(this, x => x.Conversation)
			.DisposeWith(_disposables);
#pragma warning restore CS8602 // Dereference of a possibly null reference.
	}

	public void Dispose() => _disposables.Dispose();
}

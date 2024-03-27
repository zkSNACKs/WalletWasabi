using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using WalletWasabi.BuyAnything;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Buy.Workflows.ShopinBit;

public sealed partial class ShopinBitWorkflow : Workflow
{
	private readonly BuyAnythingManager _buyAnythingManager;
	private readonly Wallet _wallet;

	private CancellationToken? _token;

	public ShopinBitWorkflow(Wallet wallet, Conversation conversation) : base(conversation)
	{
		_wallet = wallet;
		_buyAnythingManager = Services.HostedServices.Get<BuyAnythingManager>();
		IsCompleted = conversation.ConversationStatus >= ConversationStatus.Finished;
	}

	public override IMessageEditor MessageEditor => new ShopinBitMessageEditor(this, _token);

	public override async Task ExecuteAsync(CancellationToken token)
	{
		_token = token;

		// Initial message + Select Product
		await ExecuteStepAsync(new WelcomeStep(Conversation, token), token);

		// Select Country
		await ExecuteStepAsync(new CountryStep(Conversation, _buyAnythingManager.Countries, token), token);

		// Specify your request
		await ExecuteStepAsync(new RequestedItemStep(Conversation, token), token);

		// Accept Privacy Policy
		await ExecuteStepAsync(new PrivacyPolicyStep(Conversation, token), token);

		// Start Conversation (only if it's a new Conversation)
		await ExecuteStepAsync(new StartConversationStep(Conversation, _wallet, token), token);

		// Save the entire conversation
		await ExecuteStepAsync(new SaveConversationStep(Conversation, token), token);

		using (ListenToServerUpdates())
		{
			// Wait until the Offer is received
			while (!Conversation.MetaData.OfferReceived)
			{
				// User might send chat messages to Support Agent
				await ExecuteStepAsync(new SupportChatStep(Conversation, token), token);
			}
		}

		// Firstname
		await ExecuteStepAsync(new FirstNameStep(Conversation, token), token);

		// Lastname
		await ExecuteStepAsync(new LastNameStep(Conversation, token), token);

		// Streetname
		await ExecuteStepAsync(new StreetNameStep(Conversation, token), token);

		// Housenumber
		await ExecuteStepAsync(new HouseNumberStep(Conversation, token), token);

		// ZIP/Postalcode
		await ExecuteStepAsync(new ZipPostalCodeStep(Conversation, token), token);

		// City
		await ExecuteStepAsync(new CityStep(Conversation, token), token);

		// State
		await ExecuteStepAsync(new StateStep(Conversation, token), token);

		// Accept Terms of service
		await ExecuteStepAsync(new ConfirmTosStep(Conversation, token), token);

		// Accept Offer
		await ExecuteStepAsync(new AcceptOfferStep(Conversation, token), token);

		// Save Conversation
		await ExecuteStepAsync(new SaveConversationStep(Conversation, token), token);

		using (ListenToServerUpdates())
		{
			// Wait until the Conversation is deleted on SIB side
			while (Conversation.ConversationStatus != ConversationStatus.Deleted)
			{
				if (Conversation.ConversationStatus == ConversationStatus.Finished && !IsCompleted)
				{
					IsCompleted = true;
					await ExecuteStepAsync(new OrderFinishedMessage(Conversation, token), token);
				}

				// User might send chat messages to Support Agent
				await ExecuteStepAsync(new SupportChatStep(Conversation, token), token);
			}
		}

		WorkflowCompleted();
	}

	/// <summary>
	/// Listen to Conversation Updates from the Server. Upon that, it updates the Conversation
	/// </summary>
	/// <returns>An <see cref="IDisposable"/> for the event subscription.</returns>
	/// <remarks>If the ConversationStatus changes, this handler will Ignore the Current Step</remarks>
	/// <exception cref="InvalidOperationException"></exception>
	private IDisposable ListenToServerUpdates()
	{
		return
			Observable.FromEventPattern<ConversationUpdateEvent>(_buyAnythingManager, nameof(BuyAnythingManager.ConversationUpdated))
					  .Where(x => x.EventArgs.Conversation.Id == Conversation.Id)
					  .ObserveOn(RxApp.MainThreadScheduler)
					  .Do(x =>
					  {
						  var oldStatus = Conversation.ConversationStatus;
						  var newStatus = x.EventArgs.Conversation.ConversationStatus;

						  Conversation = x.EventArgs.Conversation;

						  if (oldStatus != newStatus)
						  {
							  CurrentStep?.Ignore();
						  }
					  })
					  .Subscribe()
					  .DisposeWith(Disposables);
	}
}

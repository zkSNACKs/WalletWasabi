using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.BuyAnything;
using WalletWasabi.Fluent.ViewModels.Wallets.Buy.Workflows;
using WalletWasabi.Fluent.ViewModels.Wallets.Buy.Workflows.ShopinBit;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.Models.Wallets;

[AutoInterface]
public partial class BuyAnythingModel(Wallet wallet)
{
	private const string NewOrderTitle = "New Order";
	private readonly Lazy<BuyAnythingManager> _buyAnythingManager = new Lazy<BuyAnythingManager>(() => Services.HostedServices.Get<BuyAnythingManager>());

	private BuyAnythingManager BuyAnythingManager => _buyAnythingManager.Value;

	public Workflow CreateWorkflow(Conversation conversation)
	{
		// If another type of workflow is required in the future this is the place where it should be defined
		var workflow = new ShopinBitWorkflow(wallet, conversation);

		return workflow;
	}

	public Conversation NewConversation()
	{
		return new Conversation(ConversationId.Empty, Chat.Empty, OrderStatus.Open, ConversationStatus.Started, new ConversationMetaData(NewOrderTitle));
	}

	public async Task<Conversation[]> GetConversationsAsync(CancellationToken cancellationToken)
	{
		return await BuyAnythingManager.GetConversationsAsync(wallet, cancellationToken);
	}

	public async Task<Conversation> StartConversationAsync(Conversation conversation, CancellationToken cancellationToken)
	{
		return await BuyAnythingManager.StartNewConversationAsync(wallet, conversation, cancellationToken);
	}

	public async Task RemoveConversationByIdAsync(ConversationId conversationId, CancellationToken cancellationToken)
	{
		await BuyAnythingManager.RemoveConversationsByIdsAsync([conversationId], cancellationToken);
	}
}

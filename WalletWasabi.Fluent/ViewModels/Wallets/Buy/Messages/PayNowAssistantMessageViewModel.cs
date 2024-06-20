using System.Reactive.Linq;
using ReactiveUI;
using System.Threading.Tasks;
using System.Windows.Input;
using WalletWasabi.BuyAnything;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Fluent.ViewModels.Wallets.Buy.Workflows;
using WalletWasabi.Fluent.ViewModels.Wallets.Send;
using WalletWasabi.Logging;
using WalletWasabi.Blockchain.TransactionBuilding;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Buy.Messages;

public partial class PayNowAssistantMessageViewModel : AssistantMessageViewModel
{
	private readonly IWalletModel _wallet;
	private readonly Workflow _workflow;

	[AutoNotify] private bool _isBusy;

	public PayNowAssistantMessageViewModel(UiContext uiContext, IWalletModel wallet, Workflow workflow, ChatMessage message) : base(message)
	{
		if (message.Data is not Invoice invoice)
		{
			throw new InvalidOperationException($"Invalid Data Type: {message.Data?.GetType().Name}");
		}

		_workflow = workflow;

		Invoice = invoice;
		Amount = invoice.Amount;
		Address = invoice.BitcoinAddress;
		IsPaid = invoice.IsPaid;
		PaymentUrl = invoice.Bip21Link;
		PayButtonText = IsPaid ? "Paid" : "Pay";

		UiContext = uiContext;
		_wallet = wallet;
		PayNowCommand = ReactiveCommand.CreateFromTask(PayNowAsync, this.WhenAnyValue(x => x.IsPaid).Select(x => !x));
	}

	public string PayButtonText { get; set; }

	public string PaymentUrl { get; set; }

	public Invoice Invoice { get; }

	public UiContext UiContext { get; }

	public decimal Amount { get; }

	public string Address { get; }

	public bool IsPaid { get; }

	public ICommand PayNowCommand { get; }

	private async Task PayNowAsync()
	{
		var transactionInfo = _wallet.Transactions.Create(Address, Amount, "Buy Anything Agent");
		await SendAsync(transactionInfo);
	}

	private async Task SendAsync(TransactionInfo info)
	{
		try
		{
			var transaction = await _wallet.Transactions.BuildTransactionForSIBAsync(info);

			var authResult = await AuthorizeAsync(transaction);
			if (authResult)
			{
				IsBusy = true;

				await _wallet.Transactions.SendAsync(transaction);

				var updatedMessage = Message with { Data = Invoice with { IsPaid = true } };
				var updatedConversation = _workflow.Conversation.ReplaceMessage(Message, updatedMessage);
				_workflow.Conversation = updatedConversation;
			}
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
			await UiContext.Navigate().CompactDialogScreen.ShowErrorAsync("Transaction", ex.ToUserFriendlyString(), "Wasabi was unable to send your transaction.");
		}
		finally
		{
			IsBusy = false;
		}
	}

	private async Task<bool> AuthorizeAsync(BuildTransactionResult transaction)
	{
		if (_wallet.IsHardwareWallet || !_wallet.Auth.HasPassword) // Do not show authentication dialog when password is empty
		{
			return true;
		}

		var authDialog = AuthorizationHelpers.GetAuthorizationDialog(_wallet, transaction);
		var authDialogResult = await UiContext.Navigate().NavigateDialogAsync(authDialog, authDialog.DefaultTarget, NavigationMode.Clear);

		return authDialogResult.Result;
	}
}

namespace WalletWasabi.Fluent.ViewModels.Wallets.Buy.Workflows.ShopinBit;

public abstract partial class TextInputWorkflowInputValidatorViewModel : WorkflowInputValidatorViewModel
{
	protected TextInputWorkflowInputValidatorViewModel(
		IWorkflowValidator workflowValidator, 
		string? message, 
		string? watermark) : base(workflowValidator, message, watermark)
	{
	}
}
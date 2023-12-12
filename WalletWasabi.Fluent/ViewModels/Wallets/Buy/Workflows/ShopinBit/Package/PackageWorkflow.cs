using System.Collections.Generic;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Buy.Workflows.ShopinBit;

public sealed partial class PackageWorkflow : Workflow
{
	private readonly PackageWorkflowRequest _request;

	public PackageWorkflow(WorkflowState workflowState)
	{
		_request = new PackageWorkflowRequest();

		// TODO:
		var trackingUrl = "www.trackmypackage.com/trcknmbr0000001";
		var downloadUrl = "www.invoice.com/lamboincoice";

		Steps = new List<WorkflowStep>
		{
			// Download
			new (false,
				new DefaultInputValidator(
					workflowState,
					() => "Download your files:")),
			// Download links
			new(false,
				new DefaultInputValidator(
					workflowState,
					() => $"{downloadUrl}")),
			// Shipping
			new(false,
				new DefaultInputValidator(
					workflowState,
					() => "For shipping updates:")),
			// Shipping link
			new(false,
				new DefaultInputValidator(
					workflowState,
					() => $"{trackingUrl}")),
			// 30 day message
			new(false,
				new DefaultInputValidator(
					workflowState,
					() => "I'll be available for the next 30 days to assist with any questions you might have.")),
		};

		CreateCanEditObservable();
	}
}
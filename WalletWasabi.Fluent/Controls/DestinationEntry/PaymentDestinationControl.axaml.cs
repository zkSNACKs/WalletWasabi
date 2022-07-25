using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using WalletWasabi.Fluent.Controls.DestinationEntry.ViewModels;

namespace WalletWasabi.Fluent.Controls.DestinationEntry
{
	public partial class PaymentDestinationControl : UserControl
	{
		private decimal _conversionRate;

		public static readonly DirectProperty<PaymentDestinationControl, decimal> ConversionRateProperty = AvaloniaProperty.RegisterDirect<PaymentDestinationControl, decimal>(
			"ConversionRate",
			o => o.ConversionRate,
			(o, v) => o.ConversionRate = v);

		private PaymentViewModel _paymentController;

		public static readonly DirectProperty<PaymentDestinationControl, PaymentViewModel> PaymentControllerProperty = AvaloniaProperty.RegisterDirect<PaymentDestinationControl, PaymentViewModel>(
			"PaymentController",
			o => o.PaymentController,
			(o, v) => o.PaymentController = v);

		public PaymentViewModel PaymentController
		{
			get => _paymentController;
			set => SetAndRaise(PaymentControllerProperty, ref _paymentController, value);
		}

		public decimal ConversionRate
		{
			get => _conversionRate;
			set => SetAndRaise(ConversionRateProperty, ref _conversionRate, value);
		}

		public PaymentDestinationControl()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private ScanQrViewModel _scanQrController;

		public static readonly DirectProperty<PaymentDestinationControl, ScanQrViewModel> ScanQrControllerProperty = AvaloniaProperty.RegisterDirect<PaymentDestinationControl, ScanQrViewModel>(
			"ScanQrController",
			o => o.ScanQrController,
			(o, v) => o.ScanQrController = v);

		public ScanQrViewModel ScanQrController
		{
			get => _scanQrController;
			set => SetAndRaise(ScanQrControllerProperty, ref _scanQrController, value);
		}

		private PasteButtonViewModel _pasteController;

		public static readonly DirectProperty<PaymentDestinationControl, PasteButtonViewModel> PasteControllerProperty = AvaloniaProperty.RegisterDirect<PaymentDestinationControl, PasteButtonViewModel>(
			"PasteController",
			o => o.PasteController,
			(o, v) => o.PasteController = v);

		public PasteButtonViewModel PasteController
		{
			get => _pasteController;
			set => SetAndRaise(PasteControllerProperty, ref _pasteController, value);
		}
	}
}

using NBitcoin;

namespace WalletWasabi.Rpc
{
	public class PaymentInfo
	{
		public BitcoinAddress Sendto { get; set; }
		public Money Amount { get; set; }
		public string Label { get; set; }
		public bool SubtractFee { get; set; }
	}
}

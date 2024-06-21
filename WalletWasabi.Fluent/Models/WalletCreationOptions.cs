using NBitcoin;
using WalletWasabi.Hwi.Models;

namespace WalletWasabi.Fluent.Models;

public abstract record WalletCreationOptions(string? WalletName = null, string? Passphrase = null)
{
	public record AddNewWallet(string? WalletName = null, string? Passphrase = null, Mnemonic? Mnemonic = null)
		: WalletCreationOptions(WalletName, Passphrase)
	{
		public AddNewWallet WithNewMnemonic()
		{
			return this with { Mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve) };
		}
	}

	public record ConnectToHardwareWallet(string? WalletName = null, HwiEnumerateEntry? Device = null)
		: WalletCreationOptions(WalletName);

	public record ImportWallet(string? WalletName = null, string? FilePath = null)
		: WalletCreationOptions(WalletName);

	public record RecoverWallet(string? WalletName = null, string? Passphrase = null, Mnemonic? Mnemonic = null, int? MinGapLimit = null, string? AccountKeyPath = null)
		: WalletCreationOptions(WalletName, Passphrase);
}

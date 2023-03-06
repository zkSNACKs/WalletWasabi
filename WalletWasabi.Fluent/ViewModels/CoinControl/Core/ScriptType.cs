namespace WalletWasabi.Fluent.ViewModels.CoinControl.Core;

public record ScriptType(string Name, string ShortName)
{
	public static readonly ScriptType Unknown = new("Unknown", "?");
	public static ScriptType SegWit = new("SegWit", "SW");
	public static ScriptType NativeSegWit = new("Native SegWit (Bech32)", "NS");
	public static ScriptType Taproot = new("Taproot (Bech32m)", "TR");

	public static ScriptType FromEnum(NBitcoin.ScriptType type)
	{
		return type switch
		{
			NBitcoin.ScriptType.Witness => Unknown,
			NBitcoin.ScriptType.P2PKH => Unknown,
			NBitcoin.ScriptType.P2SH => Unknown,
			NBitcoin.ScriptType.P2PK => Unknown,
			NBitcoin.ScriptType.MultiSig => Unknown,
			NBitcoin.ScriptType.P2WSH => SegWit,
			NBitcoin.ScriptType.P2WPKH => NativeSegWit,
			NBitcoin.ScriptType.Taproot => Taproot,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
		};
	}
}

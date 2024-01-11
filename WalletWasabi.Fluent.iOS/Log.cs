using System;

namespace WalletWasabi.Fluent.IOS;

public static class Log
{
	public static int Error(string? tag, string msg)
	{
		Console.WriteLine($"[{tag}] {msg}");
		return 0;
	}
}

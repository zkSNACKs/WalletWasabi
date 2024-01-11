using System;
using UIKit;

namespace WalletWasabi.Fluent.IOS;

public class Application
{
	// This is the main entry point of the application.
	private static void Main(string[] args)
	{
		try
		{
			// if you want to use a different Application Delegate class from "AppDelegate"
			// you can specify it here.
			UIApplication.Main(args, null, typeof(AppDelegate));
		}
		catch (Exception e)
		{
			Log.Error("WASABI", $"{e}");
		}
	}
}

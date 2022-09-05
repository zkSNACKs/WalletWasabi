﻿using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WalletWasabi.Fluent.DebuggerTools.ViewModels;
using WalletWasabi.Fluent.DebuggerTools.Views;

namespace WalletWasabi.Fluent.DebuggerTools;

internal static class DebuggerTools
{
	public static void AttachDebuggerTools(this TopLevel root)
	{
		AttachDebuggerTools(root, new KeyGesture(Key.F5));
	}

	public static void AttachDebuggerTools(this TopLevel root, KeyGesture gesture)
	{
		async void Handler(object? sender, KeyEventArgs args)
		{
			if (gesture.Matches(args))
			{
				var debuggerViewModel = new DebuggerViewModel();

				var window = new DebuggerWindow
				{
					DataContext = debuggerViewModel
				};

				// window.Show(root as Window);
				window.Show();

				await Task.Run(() => debuggerViewModel.Initialize());
			}
		}

		root.AddHandler(InputElement.KeyDownEvent, Handler, RoutingStrategies.Tunnel);
	}
}

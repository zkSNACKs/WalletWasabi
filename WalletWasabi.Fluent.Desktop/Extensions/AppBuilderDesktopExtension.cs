using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Dialogs;
using Avalonia.Media;

namespace WalletWasabi.Fluent.Desktop.Extensions;

public static class AppBuilderDesktopExtension
{
	public static AppBuilder SetupAppBuilder(AppBuilder appBuilder)
	{
		bool enableGpu = Services.PersistentConfig is null ? false : Services.PersistentConfig.EnableGpu;

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			appBuilder
				.UseWin32()
				.UseSkia();
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			appBuilder.UsePlatformDetect()
				.UseManagedSystemDialogs<Window>();
		}
		else
		{
			appBuilder.UsePlatformDetect();
		}

		return appBuilder
			.WithInterFont()
			.With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter, $Default" })
			.With(new SkiaOptions { MaxGpuResourceSizeBytes = 2560 * 1600 * 4 * 12 })
			.With(new Win32PlatformOptions
			{
				RenderingMode = enableGpu
					? new[] { Win32RenderingMode.AngleEgl, Win32RenderingMode.Software }
					: new[] { Win32RenderingMode.Software },
				CompositionMode = new[] { Win32CompositionMode.WinUIComposition, Win32CompositionMode.RedirectionSurface }
			})
			.With(new X11PlatformOptions
			{
				RenderingMode = enableGpu
					? new[] { X11RenderingMode.Glx, X11RenderingMode.Software }
					: new[] { X11RenderingMode.Software },
				WmClass = "Wasabi Wallet"
			})
			.With(new AvaloniaNativePlatformOptions
			{
				RenderingMode = enableGpu
					? new[] { AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software }
					: new[] { AvaloniaNativeRenderingMode.Software },
			})
			.With(new MacOSPlatformOptions { ShowInDock = true });
	}
}

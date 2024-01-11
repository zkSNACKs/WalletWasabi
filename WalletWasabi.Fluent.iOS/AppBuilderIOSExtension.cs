using Avalonia;
using Avalonia.Media;

namespace WalletWasabi.Fluent.IOS;

public static class AppBuilderIOSExtension
{
	public static AppBuilder SetupAppBuilder(AppBuilder appBuilder)
	{
		appBuilder.UseiOS();

		return appBuilder
			.WithInterFont()
			.With(new SkiaOptions { MaxGpuResourceSizeBytes = 2560 * 1600 * 4 * 12 });

		// TODO: With FontManagerOptions crashes the app

		// TODO: No iOS PlatformOptions to use enableGpu flag.

		// bool enableGpu = Services.PersistentConfig is null ? false : Services.PersistentConfig.EnableGpu;

		// return appBuilder
		// 	.WithInterFont()
		// 	.With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter, $Default" })
		// 	.With(new SkiaOptions { MaxGpuResourceSizeBytes = 2560 * 1600 * 4 * 12 });
		// 	.With(new IOSPlatformOptions
		// 	{
		// 		RenderingMode = enableGpu
		// 			? new[] { AndroidRenderingMode.Egl, AndroidRenderingMode.Software }
		// 			: new[] { AndroidRenderingMode.Software },
		// 	});
	}
}

using Avalonia;
using Avalonia.Media;

namespace WalletWasabi.Fluent.Android;

public static class AppBuilderAndroidExtension
{
	public static AppBuilder SetupAppBuilder(AppBuilder appBuilder)
	{
		bool enableGpu = Services.PersistentConfig is null ? false : Services.PersistentConfig.EnableGpu;

		appBuilder.UseAndroid();

		return appBuilder
			.WithInterFont()
			.With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter, $Default" })
			.With(new SkiaOptions { MaxGpuResourceSizeBytes = 2560 * 1600 * 4 * 12 })
			.With(new AndroidPlatformOptions
			{
				RenderingMode = enableGpu
					? new[] { AndroidRenderingMode.Egl, AndroidRenderingMode.Software }
					: new[] { AndroidRenderingMode.Software },
			});
	}
}

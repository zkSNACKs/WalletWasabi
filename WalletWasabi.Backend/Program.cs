using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;
using WalletWasabi.Logging;

namespace WalletWasabi.Backend;

public static class Program
{
	public static async Task Main(string[] args)
	{
		try
		{
			using var host = CreateHostBuilder(args).Build();
			await host.RunWithTasksAsync();
		}
		catch (Exception ex)
		{
			Logger.LogCritical(ex);
		}
	}

	public static IHostBuilder CreateHostBuilder(string[] args) =>
		Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(webBuilder => webBuilder
			.UseStartup<Startup>()
			.ConfigureKestrel(options => options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(3)) // Default is 130 seconds.
			.UseUrls(Environment.GetEnvironmentVariable("WASABI_BIND") ?? "http://localhost:37127/"));
}

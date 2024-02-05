using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using NBitcoin;
using NBitcoin.RPC;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using WalletWasabi.Affiliation;
using WalletWasabi.Backend.Middlewares;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Cache;
using WalletWasabi.Helpers;
using WalletWasabi.Interfaces;
using WalletWasabi.Logging;
using WalletWasabi.Userfacing;
using WalletWasabi.WabiSabi;
using WalletWasabi.WabiSabi.Models.Serialization;
using WalletWasabi.WebClients;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.Net;

[assembly: ApiController]

namespace WalletWasabi.Backend;

public class Startup
{
	public Startup(IConfiguration configuration)
	{
		Configuration = configuration;
	}

	public IConfiguration Configuration { get; }

	// This method gets called by the runtime. Use this method to add services to the container.
	public void ConfigureServices(IServiceCollection services)
	{
		string dataDir = Configuration["datadir"] ?? EnvironmentHelpers.GetDataDir(Path.Combine("WalletWasabi", "Backend"));

		services.AddMemoryCache();

		services.AddMvc(options =>
			{
				options.ModelMetadataDetailsProviders.Add(new SuppressChildValidationMetadataProvider(typeof(BitcoinAddress)));
				options.ModelMetadataDetailsProviders.Add(new SuppressChildValidationMetadataProvider(typeof(Script)));
			})
			.AddControllersAsServices();

		services.AddMvc()
			.AddNewtonsoftJson();

		services.AddControllers().AddNewtonsoftJson(x => x.SerializerSettings.Converters = JsonSerializationOptions.Default.Settings.Converters);

		// Register the Swagger generator, defining one or more Swagger documents
		services.AddSwaggerGen(c =>
		{
			c.SwaggerDoc(
				$"v{Constants.BackendMajorVersion}",
				new OpenApiInfo
				{
					Version = $"v{Constants.BackendMajorVersion}",
					Title = "Wasabi Wallet API",
					Description = "Privacy focused Bitcoin Web API.",
					License = new OpenApiLicense { Name = "Use under MIT.", Url = new Uri("https://github.com/zkSNACKs/WalletWasabi/blob/master/LICENSE.md") }
				});

			// Set the comments path for the Swagger JSON and UI.
			var basePath = AppContext.BaseDirectory;
			var xmlPath = Path.Combine(basePath, "WalletWasabi.Backend.xml");
			c.IncludeXmlComments(xmlPath);
		});

		services.AddLogging(logging => logging.AddFilter((s, level) => level >= Microsoft.Extensions.Logging.LogLevel.Warning));

		services.AddSingleton<IExchangeRateProvider>(new ExchangeRateProvider());
		services.AddSingleton(serviceProvider =>
		{
			string configFilePath = Path.Combine(dataDir, "Config.json");
			Config config = new(configFilePath);
			config.LoadFile(createIfMissing: true);
			return config;
		});

		services.AddSingleton<IdempotencyRequestCache>();
		services.AddHttpClient(AffiliationConstants.LogicalHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
		{
			// See https://github.com/dotnet/runtime/issues/18348#issuecomment-415845645
			PooledConnectionLifetime = TimeSpan.FromMinutes(5)
		});
		services.AddSingleton(serviceProvider =>
		{
			Config config = serviceProvider.GetRequiredService<Config>();
			string host = config.GetBitcoinCoreRpcEndPoint().ToString(config.Network.RPCPort);
			IHttpClientFactory httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

			RPCClient rpcClient = new(
					authenticationString: config.BitcoinRpcConnectionString,
					hostOrUri: host,
					network: config.Network);

			IMemoryCache memoryCache = serviceProvider.GetRequiredService<IMemoryCache>();
			CachedRpcClient cachedRpc = new(rpcClient, memoryCache);

			return new Global(dataDir, cachedRpc, config, httpClientFactory);
		});
		services.AddSingleton(serviceProvider =>
		{
			var global = serviceProvider.GetRequiredService<Global>();
			var coordinator = global.HostedServices.Get<WabiSabiCoordinator>();
			return coordinator.Arena;
		});
		services.AddSingleton(serviceProvider =>
		{
			var global = serviceProvider.GetRequiredService<Global>();
			var coordinator = global.HostedServices.Get<WabiSabiCoordinator>();
			return coordinator.CoinJoinFeeRateStatStore;
		});
		services.AddSingleton(serviceProvider =>
		{
			var global = serviceProvider.GetRequiredService<Global>();
			var coordinator = global.HostedServices.Get<WabiSabiCoordinator>();
			return coordinator.AffiliationManager;
		});
		services.AddSingleton(serviceProvider =>
		{
			var global = serviceProvider.GetRequiredService<Global>();
			return global.CoinJoinMempoolManager;
		});
		services.AddSingleton(serviceProvider =>
		{
			var global = serviceProvider.GetRequiredService<Global>();
			return global.IndexBuilderService;
		});
		services.AddStartupTask<InitConfigStartupTask>();

		services.AddResponseCompression();

		services.AddWebSocketHandlers();
	}

	[SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "This method gets called by the runtime. Use this method to configure the HTTP request pipeline")]
	public void Configure(IApplicationBuilder app, IWebHostEnvironment env, Global global)
	{
		// Enable middleware to serve generated Swagger as a JSON endpoint.
		app.UseSwagger();

		// Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.), specifying the Swagger JSON endpoint.
		app.UseSwaggerUI(c => c.SwaggerEndpoint($"/swagger/v{Constants.BackendMajorVersion}/swagger.json", $"Wasabi Wallet API V{Constants.BackendMajorVersion}"));

		app.UseRouting();

		// So to correctly handle HEAD requests.
		// https://www.tpeczek.com/2017/10/exploring-head-method-behavior-in.html
		// https://github.com/tpeczek/Demo.AspNetCore.Mvc.CosmosDB/blob/master/Demo.AspNetCore.Mvc.CosmosDB/Middlewares/HeadMethodMiddleware.cs
		app.UseMiddleware<HeadMethodMiddleware>();

		// Enables websocket
		var webSocketOptions = new WebSocketOptions();
		webSocketOptions.AllowedOrigins.Add("*");
		app.UseWebSockets(webSocketOptions);

		var serviceScopeFactory = app.ApplicationServices.GetRequiredService<IServiceScopeFactory>();
		var serviceProvider = serviceScopeFactory.CreateScope().ServiceProvider;
		app.MapWebSocketManager("/satoshi", serviceProvider.GetService<SatoshiWebSocketHandler>());

		app.UseResponseCompression();

		app.UseEndpoints(endpoints => endpoints.MapControllers());

		var applicationLifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
		applicationLifetime.ApplicationStopped.Register(() => OnShutdown(global)); // Don't register async, that won't hold up the shutdown
	}

	private void OnShutdown(Global global)
	{
		global.Dispose();
		Logger.LogSoftwareStopped("Wasabi Backend");
	}
}

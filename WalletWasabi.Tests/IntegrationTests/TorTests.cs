using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.Tor;
using WalletWasabi.Tor.Http;
using WalletWasabi.Tor.Socks5;
using Xunit;
using Logger = WalletWasabi.Logging.Logger;

namespace WalletWasabi.Tests.IntegrationTests
{
	public class TorTests : IAsyncLifetime
	{
		public TorTests()
		{
			Logger.SetMinimumLevel(Logging.LogLevel.Trace);
			TorSocks5ClientPool = TorSocks5ClientPool.Create(Common.TorSocks5Endpoint);
		}

		private TorSocks5ClientPool TorSocks5ClientPool { get; }

		public async Task InitializeAsync()
		{
			var torManager = new TorProcessManager(Common.TorSettings, Common.TorSocks5Endpoint);
			bool started = await torManager.StartAsync(ensureRunning: true);
			Assert.True(started, "Tor failed to start.");
		}

		public Task DisposeAsync()
		{
			return Task.CompletedTask;
		}

		/// <summary>
		/// TODO: Kill me with fire before merge. Thank you.
		/// </summary>
		[Fact]
		public async Task TestMultipleHttpsAsync()
		{
			Logger.SetMinimumLevel(Logging.LogLevel.Trace);
			var client = MakeTorHttpClient(new Uri("https://docs.postman-echo.com/"));

			HttpResponseMessage[] httpResponseMessages = new HttpResponseMessage[] {
				await client.SendAsync(HttpMethod.Get, "/"),
				await client.SendAsync(HttpMethod.Get, "/"),
			};

			int i = 0;
			foreach (HttpResponseMessage response in httpResponseMessages)
			{
				i++;

				string message = await response.Content.ReadAsStringAsync();
				string excerpt = message.Substring(0, Math.Min(100, message.Length));

				Logger.LogDebug($"#{i}: {excerpt}");
			}

			Assert.True(true);
		}

		/// <summary>
		/// TODO: Kill me with fire before merge. Thank you.
		/// </summary>
		[Fact]
		public async Task PerfTestAsync()
		{
			Logger.SetMinimumLevel(Logging.LogLevel.Trace);
			var client = MakeTorHttpClient(new Uri("http://api.qbit.ninja"));

			using HttpRequestMessage httpRequest = new(HttpMethod.Get, new Uri("http://api.qbit.ninja"));
			var tasks = new List<Task<HttpResponseMessage>>
			{
				//client.SendAsync(httpRequest, isolateStream: false),
				client.SendAsync(httpRequest, isolateStream: true),
				client.SendAsync(httpRequest, isolateStream: true),
				client.SendAsync(httpRequest, isolateStream: true),
				//client.SendAsync(httpRequest, isolateStream: false),
				//client.SendAsync(httpRequest, isolateStream: false),
				//client.SendAsync(httpRequest, isolateStream: false),
				//client.SendAsync(httpRequest, isolateStream: false),
				//client.SendAsync(httpRequest, isolateStream: false),
				//client.SendAsync(httpRequest, isolateStream: false),
				//client.SendAsync(httpRequest, isolateStream: false)
			};

			HttpResponseMessage[] httpResponseMessages = await Task.WhenAll(tasks);

			int i = 0;
			foreach (HttpResponseMessage response in httpResponseMessages)
			{
				i++;

				string message = await response.Content.ReadAsStringAsync();
				string excerpt = message.Substring(0, Math.Min(100, message.Length));

				Logger.LogDebug($"#{i}: {excerpt}");
			}

			Assert.True(true);
		}

		[Fact]
		public async Task CanDoRequestManyDifferentAsync()
		{
			var client = MakeTorHttpClient(new Uri("http://api.qbit.ninja"));
			await QBitTestAsync(client, 10, alterRequests: true);
		}

		[Fact]
		public async Task CanRequestChunkEncodedAsync()
		{
			var client = MakeTorHttpClient(new Uri("http://anglesharp.azurewebsites.net/"));
			var response = await client.SendAsync(HttpMethod.Get, "Chunked");
			var content = await response.Content.ReadAsStringAsync();
			Assert.Contains("Chunked transfer encoding test", content);
			Assert.Contains("This is a chunked response after 100 ms.", content);
			Assert.Contains("This is a chunked response after 1 second. The server should not close the stream before all chunks are sent to a client.", content);
		}

		[Fact]
		public async Task CanDoBasicPostHttpRequestAsync()
		{
			var client = MakeTorHttpClient(new Uri("http://postman-echo.com"));
			HttpContent content = new StringContent("This is expected to be sent back as part of response body.");

			HttpResponseMessage message = await client.SendAsync(HttpMethod.Post, "post", content);
			var responseContentString = await message.Content.ReadAsStringAsync();

			Assert.Contains("{\"args\":{},\"data\":\"This is expected to be sent back as part of response body.\"", responseContentString);
		}

		[Fact]
		public async Task CanDoBasicPostHttpsRequestAsync()
		{
			var client = MakeTorHttpClient(new Uri("https://postman-echo.com"));
			HttpContent content = new StringContent("This is expected to be sent back as part of response body.");

			HttpResponseMessage message = await client.SendAsync(HttpMethod.Post, "post", content);
			var responseContentString = await message.Content.ReadAsStringAsync();

			Assert.Contains("{\"args\":{},\"data\":\"This is expected to be sent back as part of response body.\"", responseContentString);
		}

		[Fact]
		public async Task TorIpIsNotTheRealOneAsync()
		{
			var requestUri = "https://api.ipify.org/";
			IPAddress? realIp;
			IPAddress? torIp;

			// 1. Get real IP
			using (var httpClient = new HttpClient())
			{
				var content = await (await httpClient.GetAsync(requestUri)).Content.ReadAsStringAsync();
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out realIp);
				Assert.True(gotIp);
			}

			// 2. Get Tor IP
			{
				var client = MakeTorHttpClient(new Uri(requestUri));
				var content = await (await client.SendAsync(HttpMethod.Get, "")).Content.ReadAsStringAsync();
				var gotIp = IPAddress.TryParse(content.Replace("\n", ""), out torIp);
				Assert.True(gotIp);
			}

			Assert.NotEqual(realIp, torIp);
		}

		[Fact]
		public async Task CanDoHttpsAsync()
		{
			var client = MakeTorHttpClient(new Uri("https://postman-echo.com"));
			var content = await (await client.SendAsync(HttpMethod.Get, "get?foo1=bar1&foo2=bar2")).Content.ReadAsStringAsync();

			Assert.Contains("{\"args\":{\"foo1\":\"bar1\",\"foo2\":\"bar2\"}", content);
		}

		[Fact]
		public async Task CanDoIpAddressAsync()
		{
			var client = MakeTorHttpClient(new Uri("http://172.217.6.142"));
			var content = await (await client.SendAsync(HttpMethod.Get, "")).Content.ReadAsStringAsync();

			Assert.NotEmpty(content);
		}

		[Fact]
		public async Task CanRequestInRowAsync()
		{
			var client = MakeTorHttpClient(new Uri("http://api.qbit.ninja"));
			await (await client.SendAsync(HttpMethod.Get, "/transactions/38d4cfeb57d6685753b7a3b3534c3cb576c34ca7344cd4582f9613ebf0c2b02a?format=json&headeronly=true")).Content.ReadAsStringAsync();
			await (await client.SendAsync(HttpMethod.Get, "/balances/15sYbVpRh6dyWycZMwPdxJWD4xbfxReeHe?unspentonly=true")).Content.ReadAsStringAsync();
			await (await client.SendAsync(HttpMethod.Get, "balances/akEBcY5k1dn2yeEdFnTMwdhVbHxtgHb6GGi?from=tip&until=336000")).Content.ReadAsStringAsync();
		}

		[Fact]
		public async Task CanRequestOnionV2Async()
		{
			var client = MakeTorHttpClient(new Uri("http://expyuzz4wqqyqhjn.onion/"));
			HttpResponseMessage response = await client.SendAsync(HttpMethod.Get, "");
			var content = await response.Content.ReadAsStringAsync();

			Assert.Equal(HttpStatusCode.OK, response.StatusCode);

			Assert.Contains("tor", content, StringComparison.OrdinalIgnoreCase);
		}

		[Fact]
		public async Task CanRequestOnionV3Async()
		{
			var client = MakeTorHttpClient(new Uri("http://www.dds6qkxpwdeubwucdiaord2xgbbeyds25rbsgr73tbfpqpt4a6vjwsyd.onion"));
			HttpResponseMessage response = await client.SendAsync(HttpMethod.Get, "");
			var content = await response.Content.ReadAsStringAsync();

			Assert.Equal(HttpStatusCode.OK, response.StatusCode);

			Assert.Contains("whonix", content, StringComparison.OrdinalIgnoreCase);
		}

		[Fact]
		public async Task DoesntIsolateStreamsAsync()
		{
			var c1 = MakeTorHttpClient(new Uri("http://api.ipify.org"));
			var c2 = MakeTorHttpClient(new Uri("http://api.ipify.org"));
			var c3 = MakeTorHttpClient(new Uri("http://api.ipify.org"));
			var t1 = c1.SendAsync(HttpMethod.Get, "");
			var t2 = c2.SendAsync(HttpMethod.Get, "");
			var t3 = c3.SendAsync(HttpMethod.Get, "");

			var ips = new HashSet<IPAddress>
				{
					IPAddress.Parse(await (await t1).Content.ReadAsStringAsync()),
					IPAddress.Parse(await (await t2).Content.ReadAsStringAsync()),
					IPAddress.Parse(await (await t3).Content.ReadAsStringAsync())
				};

			Assert.True(ips.Count < 3);
		}

		[Fact]
		public async Task IsolatesStreamsAsync()
		{
			var c1 = MakeTorHttpClient(new Uri("http://api.ipify.org"), isolateStream: true);
			var c2 = MakeTorHttpClient(new Uri("http://api.ipify.org"), isolateStream: true);
			var c3 = MakeTorHttpClient(new Uri("http://api.ipify.org"), isolateStream: true);
			var t1 = c1.SendAsync(HttpMethod.Get, "");
			var t2 = c2.SendAsync(HttpMethod.Get, "");
			var t3 = c3.SendAsync(HttpMethod.Get, "");

			var ips = new HashSet<IPAddress>
				{
					IPAddress.Parse(await (await t1).Content.ReadAsStringAsync()),
					IPAddress.Parse(await (await t2).Content.ReadAsStringAsync()),
					IPAddress.Parse(await (await t3).Content.ReadAsStringAsync())
				};

			Assert.True(ips.Count >= 2); // very rarely it fails to isolate
		}

		[Fact]
		public async Task TorRunningAsync()
		{
			Assert.True(await new TorSocks5ClientFactory(new IPEndPoint(IPAddress.Loopback, 9050)).IsTorRunningAsync());
			Assert.False(await new TorSocks5ClientFactory(new IPEndPoint(IPAddress.Loopback, 9054)).IsTorRunningAsync());
		}

		private async Task<List<string>> QBitTestAsync(TorHttpClient client, int times, bool alterRequests = false)
		{
			var relativetUri = "/whatisit/what%20is%20my%20future";

			var tasks = new List<Task<HttpResponseMessage>>();
			for (var i = 0; i < times; i++)
			{
				var task = client.SendAsync(HttpMethod.Get, relativetUri);
				if (alterRequests)
				{
					var ipClient = MakeTorHttpClient(new Uri("https://api.ipify.org/"));
					var task2 = ipClient.SendAsync(HttpMethod.Get, "/");
					tasks.Add(task2);
				}
				tasks.Add(task);
			}

			await Task.WhenAll(tasks);

			var contents = new List<string>();
			foreach (var task in tasks)
			{
				contents.Add(await (await task).Content.ReadAsStringAsync());
			}

			return contents;
		}

		private TorHttpClient MakeTorHttpClient(Uri uri, bool isolateStream = false)
		{
			return new TorHttpClient(TorSocks5ClientPool, () => uri, isolateStream);
		}
	}
}

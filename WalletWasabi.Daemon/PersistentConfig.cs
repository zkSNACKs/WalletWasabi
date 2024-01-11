using NBitcoin;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using WalletWasabi.Exceptions;
using WalletWasabi.Helpers;
using WalletWasabi.Interfaces;
using WalletWasabi.JsonConverters;
using WalletWasabi.JsonConverters.Bitcoin;

namespace WalletWasabi.Daemon;

[JsonObject(MemberSerialization.OptIn)]
public record PersistentConfig : IConfigNg
{
	public const int DefaultJsonRpcServerPort = 37128;
	public static readonly Money DefaultDustThreshold = Money.Coins(Constants.DefaultDustThreshold);

	[JsonProperty(PropertyName = "Network")]
	[System.Text.Json.Serialization.JsonPropertyName("Network")]
	[JsonConverter(typeof(NetworkJsonConverter))]
	[System.Text.Json.Serialization.JsonConverter(typeof(NetworkJsonConverterNg))]
	public Network Network { get; set; } = Network.Main;

	[DefaultValue(Constants.BackendUri)]
	[JsonProperty(PropertyName = "MainNetBackendUri", DefaultValueHandling = DefaultValueHandling.Populate)]
	[System.Text.Json.Serialization.JsonPropertyName("MainNetBackendUri")]
	public string MainNetBackendUri { get; init; } = Constants.BackendUri;

	[DefaultValue(Constants.TestnetBackendUri)]
	[JsonProperty(PropertyName = "TestNetClearnetBackendUri", DefaultValueHandling = DefaultValueHandling.Populate)]
	[System.Text.Json.Serialization.JsonPropertyName("TestNetClearnetBackendUri")]
	public string TestNetBackendUri { get; init; } = Constants.TestnetBackendUri;

	[DefaultValue("http://localhost:37127/")]
	[JsonProperty(PropertyName = "RegTestBackendUri", DefaultValueHandling = DefaultValueHandling.Populate)]
	[System.Text.Json.Serialization.JsonPropertyName("RegTestBackendUri")]
	public string RegTestBackendUri { get; init; } = "http://localhost:37127/";

	[JsonProperty(PropertyName = "MainNetCoordinatorUri", DefaultValueHandling = DefaultValueHandling.Ignore)]
	[System.Text.Json.Serialization.JsonPropertyName("MainNetCoordinatorUri")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public string? MainNetCoordinatorUri { get; init; }

	[JsonProperty(PropertyName = "TestNetCoordinatorUri", DefaultValueHandling = DefaultValueHandling.Ignore)]
	[System.Text.Json.Serialization.JsonPropertyName("TestNetCoordinatorUri")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public string? TestNetCoordinatorUri { get; init; }

	[JsonProperty(PropertyName = "RegTestCoordinatorUri", DefaultValueHandling = DefaultValueHandling.Ignore)]
	[System.Text.Json.Serialization.JsonPropertyName("RegTestCoordinatorUri")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public string? RegTestCoordinatorUri { get; init; }

	[DefaultValue(true)]
	[JsonProperty(PropertyName = "UseTor", DefaultValueHandling = DefaultValueHandling.Populate)]
	[System.Text.Json.Serialization.JsonPropertyName("UseTor")]
	public bool UseTor { get; init; } = true;

	[DefaultValue(false)]
	[JsonProperty(PropertyName = "TerminateTorOnExit", DefaultValueHandling = DefaultValueHandling.Populate)]
	[System.Text.Json.Serialization.JsonPropertyName("TerminateTorOnExit")]
	public bool TerminateTorOnExit { get; init; } = false;

	[DefaultValue(true)]
	[JsonProperty(PropertyName = "DownloadNewVersion", DefaultValueHandling = DefaultValueHandling.Populate)]
	[System.Text.Json.Serialization.JsonPropertyName("DownloadNewVersion")]
	public bool DownloadNewVersion { get; init; } = true;

	[DefaultValue(false)]
	[JsonProperty(PropertyName = "StartLocalBitcoinCoreOnStartup", DefaultValueHandling = DefaultValueHandling.Populate)]
	[System.Text.Json.Serialization.JsonPropertyName("StartLocalBitcoinCoreOnStartup")]
	public bool StartLocalBitcoinCoreOnStartup { get; init; } = false;

	[DefaultValue(true)]
	[JsonProperty(PropertyName = "StopLocalBitcoinCoreOnShutdown", DefaultValueHandling = DefaultValueHandling.Populate)]
	[System.Text.Json.Serialization.JsonPropertyName("StopLocalBitcoinCoreOnShutdown")]
	public bool StopLocalBitcoinCoreOnShutdown { get; init; } = true;

	[JsonProperty(PropertyName = "LocalBitcoinCoreDataDir")]
	[System.Text.Json.Serialization.JsonPropertyName("LocalBitcoinCoreDataDir")]
	public string LocalBitcoinCoreDataDir { get; init; } = EnvironmentHelpers.GetDefaultBitcoinCoreDataDirOrEmptyString();

	// [JsonProperty(PropertyName = "MainNetBitcoinP2pEndPoint")]
	[System.Text.Json.Serialization.JsonPropertyName("MainNetBitcoinP2pEndPoint")]
	// [JsonConverter(typeof(EndPointJsonConverter), Constants.DefaultMainNetBitcoinP2pPort)]
	[System.Text.Json.Serialization.JsonConverter(typeof(MainNetBitcoinP2pEndPointConverterNg))]
	public EndPoint MainNetBitcoinP2pEndPoint { get; init; } = new IPEndPoint(IPAddress.Loopback, Constants.DefaultMainNetBitcoinP2pPort);

	// [JsonProperty(PropertyName = "TestNetBitcoinP2pEndPoint")]
	[System.Text.Json.Serialization.JsonPropertyName("TestNetBitcoinP2pEndPoint")]
	// [JsonConverter(typeof(EndPointJsonConverter), Constants.DefaultTestNetBitcoinP2pPort)]
	[System.Text.Json.Serialization.JsonConverter(typeof(TestNetBitcoinP2pEndPointConverterNg))]
	public EndPoint TestNetBitcoinP2pEndPoint { get; init; } = new IPEndPoint(IPAddress.Loopback, Constants.DefaultTestNetBitcoinP2pPort);

	// [JsonProperty(PropertyName = "RegTestBitcoinP2pEndPoint")]
	[System.Text.Json.Serialization.JsonPropertyName("RegTestBitcoinP2pEndPoint")]
	// JsonConverter(typeof(EndPointJsonConverter), Constants.DefaultRegTestBitcoinP2pPort)]
	[System.Text.Json.Serialization.JsonConverter(typeof(RegTestBitcoinP2pEndPointConverterNg))]
	public EndPoint RegTestBitcoinP2pEndPoint { get; init; } = new IPEndPoint(IPAddress.Loopback, Constants.DefaultRegTestBitcoinP2pPort);

	[DefaultValue(false)]
	[JsonProperty(PropertyName = "JsonRpcServerEnabled", DefaultValueHandling = DefaultValueHandling.Populate)]
	[System.Text.Json.Serialization.JsonPropertyName("JsonRpcServerEnabled")]
	public bool JsonRpcServerEnabled { get; init; }

	[DefaultValue("")]
	[JsonProperty(PropertyName = "JsonRpcUser", DefaultValueHandling = DefaultValueHandling.Populate)]
	[System.Text.Json.Serialization.JsonPropertyName("JsonRpcUser")]
	public string JsonRpcUser { get; init; } = "";

	[DefaultValue("")]
	[JsonProperty(PropertyName = "JsonRpcPassword", DefaultValueHandling = DefaultValueHandling.Populate)]
	[System.Text.Json.Serialization.JsonPropertyName("JsonRpcPassword")]
	public string JsonRpcPassword { get; init; } = "";

	[JsonProperty(PropertyName = "JsonRpcServerPrefixes")]
	[System.Text.Json.Serialization.JsonPropertyName("JsonRpcServerPrefixes")]
	public string[] JsonRpcServerPrefixes { get; init; } = new[]
	{
		"http://127.0.0.1:37128/",
		"http://localhost:37128/"
	};

	[JsonProperty(PropertyName = "DustThreshold")]
	[System.Text.Json.Serialization.JsonPropertyName("DustThreshold")]
	[JsonConverter(typeof(MoneyBtcJsonConverter))]
	[System.Text.Json.Serialization.JsonConverter(typeof(MoneyBtcJsonConverterNg))]
	public Money DustThreshold { get; init; } = DefaultDustThreshold;

	[JsonProperty(PropertyName = "EnableGpu")]
	[System.Text.Json.Serialization.JsonPropertyName("EnableGpu")]
	public bool EnableGpu { get; init; } = true;

	[DefaultValue("CoinJoinCoordinatorIdentifier")]
	[JsonProperty(PropertyName = "CoordinatorIdentifier", DefaultValueHandling = DefaultValueHandling.Populate)]
	[System.Text.Json.Serialization.JsonPropertyName("CoordinatorIdentifier")]
	public string CoordinatorIdentifier { get; init; } = "CoinJoinCoordinatorIdentifier";

	public bool DeepEquals(PersistentConfig other)
	{
		return
			Network == other.Network &&
			MainNetBackendUri == other.MainNetBackendUri &&
			TestNetBackendUri == other.TestNetBackendUri &&
			RegTestBackendUri == other.RegTestBackendUri &&
			MainNetCoordinatorUri == other.MainNetCoordinatorUri &&
			TestNetCoordinatorUri == other.TestNetCoordinatorUri &&
			RegTestCoordinatorUri == other.RegTestCoordinatorUri &&
			UseTor == other.UseTor &&
			TerminateTorOnExit == other.TerminateTorOnExit &&
			DownloadNewVersion == other.DownloadNewVersion &&
			StartLocalBitcoinCoreOnStartup == other.StartLocalBitcoinCoreOnStartup &&
			StopLocalBitcoinCoreOnShutdown == other.StopLocalBitcoinCoreOnShutdown &&
			LocalBitcoinCoreDataDir == other.LocalBitcoinCoreDataDir &&
			MainNetBitcoinP2pEndPoint.Equals(other.MainNetBitcoinP2pEndPoint) &&
			TestNetBitcoinP2pEndPoint.Equals(other.TestNetBitcoinP2pEndPoint) &&
			RegTestBitcoinP2pEndPoint.Equals(other.RegTestBitcoinP2pEndPoint) &&
			JsonRpcServerEnabled == other.JsonRpcServerEnabled &&
			JsonRpcUser == other.JsonRpcUser &&
			JsonRpcPassword == other.JsonRpcPassword &&
			JsonRpcServerPrefixes.SequenceEqual(other.JsonRpcServerPrefixes) &&
			DustThreshold == other.DustThreshold &&
			EnableGpu == other.EnableGpu &&
			CoordinatorIdentifier == other.CoordinatorIdentifier;
	}

	public EndPoint GetBitcoinP2pEndPoint()
	{
		if (Network == Network.Main)
		{
			return MainNetBitcoinP2pEndPoint;
		}
		if (Network == Network.TestNet)
		{
			return TestNetBitcoinP2pEndPoint;
		}
		if (Network == Network.RegTest)
		{
			return RegTestBitcoinP2pEndPoint;
		}
		throw new NotSupportedNetworkException(Network);
	}

	public bool MigrateOldDefaultBackendUris([NotNullWhen(true)] out PersistentConfig? newConfig)
	{
		bool hasChanged = false;
		newConfig = null;

		if (MainNetBackendUri == "https://wasabiwallet.io/" || TestNetBackendUri == "https://wasabiwallet.co/")
		{
			hasChanged = true;
			newConfig = this with
			{
				MainNetBackendUri = "https://api.wasabiwallet.io/",
				TestNetBackendUri = "https://api.wasabiwallet.co/",
			};
		}

		return hasChanged;
	}
}

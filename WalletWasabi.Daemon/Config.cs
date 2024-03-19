using NBitcoin;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using WalletWasabi.Exceptions;
using WalletWasabi.Helpers;
using WalletWasabi.Models;
using WalletWasabi.Tor;
using WalletWasabi.Userfacing;

namespace WalletWasabi.Daemon;

public class Config
{
	public static readonly IDictionary EnvironmentVariables = Environment.GetEnvironmentVariables();

	public Config(PersistentConfig persistentConfig, string[] cliArgs)
	{
		PersistentConfig = persistentConfig;
		CliArgs = cliArgs;

		Data = new()
		{
			[ nameof(Network)] = (
				"The Bitcoin network to use: main, testnet, or regtest",
				GetNetworkValue("Network", PersistentConfig.Network.ToString(), cliArgs)),
			[ nameof(MainNetBackendUri)] = (
				"The backend server's URL to connect to when the Bitcoin network is main",
				GetStringValue("MainNetBackendUri", PersistentConfig.MainNetBackendUri, cliArgs)),
			[ nameof(TestNetBackendUri)] = (
				"The backend server's URL to connect to when the Bitcoin network is testnet",
				GetStringValue("TestNetBackendUri", PersistentConfig.TestNetBackendUri, cliArgs)),
			[ nameof(RegTestBackendUri)] = (
				"The backend server's URL to connect to when the Bitcoin network is regtest",
				GetStringValue("RegTestBackendUri", PersistentConfig.RegTestBackendUri, cliArgs)),
			[ nameof(MainNetCoordinatorUri)] = (
				"The coordinator server's URL to connect to when the Bitcoin network is main",
				GetNullableStringValue("MainNetCoordinatorUri", PersistentConfig.MainNetCoordinatorUri, cliArgs)),
			[ nameof(TestNetCoordinatorUri)] = (
				"The coordinator server's URL to connect to when the Bitcoin network is testnet",
				GetNullableStringValue("TestNetCoordinatorUri", PersistentConfig.TestNetCoordinatorUri, cliArgs)),
			[ nameof(RegTestCoordinatorUri)] = (
				"The coordinator server's URL to connect to when the Bitcoin network is regtest",
				GetNullableStringValue("RegTestCoordinatorUri", PersistentConfig.RegTestCoordinatorUri, cliArgs)),
			[ nameof(UseTor)] = (
				"All the communications go through the Tor network",
				GetBoolValue("UseTor", PersistentConfig.UseTor, cliArgs)),
			[ nameof(TorSocksPort)] = (
				"Tor is started to listen with the specified SOCKS5 port",
				GetLongValue("TorSocksPort", TorSettings.DefaultSocksPort, cliArgs)),
			[ nameof(TorControlPort)] = (
				"Tor is started to listen with the specified control port",
				GetLongValue("TorControlPort", TorSettings.DefaultControlPort, cliArgs)),
			[ nameof(TerminateTorOnExit)] = (
				"Stop the Tor process when Wasabi is closed",
				GetBoolValue("TerminateTorOnExit", PersistentConfig.TerminateTorOnExit, cliArgs)),
			[ nameof(DownloadNewVersion)] = (
				"Automatically download any new released version of Wasabi",
				GetBoolValue("DownloadNewVersion", PersistentConfig.DownloadNewVersion, cliArgs)),
			[ nameof(StartLocalBitcoinCoreOnStartup)] = (
				"Start a local bitcoin node when Wasabi starts",
				GetBoolValue("StartLocalBitcoinCoreOnStartup", PersistentConfig.StartLocalBitcoinCoreOnStartup, cliArgs)),
			[ nameof(StopLocalBitcoinCoreOnShutdown)] = (
				"Stop the local bitcoin node when Wasabi is closed",
				GetBoolValue("StopLocalBitcoinCoreOnShutdown", PersistentConfig.StopLocalBitcoinCoreOnShutdown, cliArgs)),
			[ nameof(LocalBitcoinCoreDataDir)] = (
				"The path of the data directory to be used by the local bitcoin node",
				GetStringValue("LocalBitcoinCoreDataDir", PersistentConfig.LocalBitcoinCoreDataDir, cliArgs)),
			[ nameof(MainNetBitcoinP2pEndPoint)] = (
				"-",
				GetEndPointValue("MainNetBitcoinP2pEndPoint", PersistentConfig.MainNetBitcoinP2pEndPoint, cliArgs)),
			[ nameof(TestNetBitcoinP2pEndPoint)] = (
				"-",
				GetEndPointValue("TestNetBitcoinP2pEndPoint", PersistentConfig.TestNetBitcoinP2pEndPoint, cliArgs)),
			[ nameof(RegTestBitcoinP2pEndPoint)] = (
				"-",
				GetEndPointValue("RegTestBitcoinP2pEndPoint", PersistentConfig.RegTestBitcoinP2pEndPoint, cliArgs)),
			[ nameof(JsonRpcServerEnabled)] = (
				"Start the Json RPC Server and accept requests",
				GetBoolValue("JsonRpcServerEnabled", PersistentConfig.JsonRpcServerEnabled, cliArgs)),
			[ nameof(JsonRpcUser)] = (
				"The user name that is authorized to make requests to the Json RPC server",
				GetStringValue("JsonRpcUser", PersistentConfig.JsonRpcUser, cliArgs)),
			[ nameof(JsonRpcPassword)] = (
				"The user password that is authorized to make requests to the Json RPC server",
				GetStringValue("JsonRpcPassword", PersistentConfig.JsonRpcPassword, cliArgs)),
			[ nameof(JsonRpcServerPrefixes)] = (
				"The Json RPC server prefixes",
				GetStringArrayValue("JsonRpcServerPrefixes", PersistentConfig.JsonRpcServerPrefixes, cliArgs)),
			[ nameof(RpcOnionEnabled)] = (
				"Publish the Json RPC Server as a Tor Onion service",
				GetBoolValue("RpcOnionEnabled", value: false, cliArgs)),
			[ nameof(DustThreshold)] = (
				"The amount threshold under which coins received from others to already used addresses are considered a dust attack",
				GetMoneyValue("DustThreshold", PersistentConfig.DustThreshold, cliArgs)),
			[ nameof(BlockOnlyMode)] = (
				"Wasabi listens only for blocks and not for transactions",
				GetBoolValue("BlockOnly", value: false, cliArgs)),
			[ nameof(LogLevel)] = (
				"The level of detail in the logs: trace, debug, info, warning, error, or critical",
				GetStringValue("LogLevel", value: "", cliArgs)),
			[ nameof(EnableGpu)] = (
				"Use a GPU to render the user interface",
				GetBoolValue("EnableGpu", PersistentConfig.EnableGpu, cliArgs)),
			[ nameof(CoordinatorIdentifier)] = (
				"-",
				GetStringValue("CoordinatorIdentifier", PersistentConfig.CoordinatorIdentifier, cliArgs)),
		};

		// Check if any config value is overridden (either by an environment value, or by a CLI argument).
		foreach ((_, IValue optionValue) in Data.Values)
		{
			if (optionValue.Overridden)
			{
				IsOverridden = true;
				break;
			}
		}

		ServiceConfiguration = new ServiceConfiguration(GetBitcoinP2pEndPoint(), DustThreshold);
	}

	private Dictionary<string, (string Hint, IValue Value)> Data { get; }
	public PersistentConfig PersistentConfig { get; }
	public string[] CliArgs { get; }
	public Network Network => GetEffectiveValue<NetworkValue, Network>(nameof(Network));

	public string MainNetBackendUri => GetEffectiveValue<StringValue, string>(nameof(MainNetBackendUri));
	public string TestNetBackendUri => GetEffectiveValue<StringValue, string>(nameof(TestNetBackendUri));
	public string RegTestBackendUri => GetEffectiveValue<StringValue, string>(nameof(RegTestBackendUri));
	public string? MainNetCoordinatorUri => GetEffectiveValue<NullableStringValue, string?>(nameof(MainNetCoordinatorUri));
	public string? TestNetCoordinatorUri => GetEffectiveValue<NullableStringValue, string?>(nameof(TestNetCoordinatorUri));
	public string? RegTestCoordinatorUri => GetEffectiveValue<NullableStringValue, string?>(nameof(RegTestCoordinatorUri));
	public bool UseTor => GetEffectiveValue<BoolValue, bool>(nameof(UseTor)) && Network != Network.RegTest;
	public int TorSocksPort => GetEffectiveValue<IntValue, int>(nameof(TorSocksPort));
	public int TorControlPort => GetEffectiveValue<IntValue, int>(nameof(TorControlPort));
	public bool TerminateTorOnExit => GetEffectiveValue<BoolValue, bool>(nameof(TerminateTorOnExit));
	public bool DownloadNewVersion => GetEffectiveValue<BoolValue, bool>(nameof(DownloadNewVersion));
	public bool StartLocalBitcoinCoreOnStartup => GetEffectiveValue<BoolValue, bool>(nameof(StartLocalBitcoinCoreOnStartup));
	public bool StopLocalBitcoinCoreOnShutdown => GetEffectiveValue<BoolValue, bool>(nameof(StopLocalBitcoinCoreOnShutdown));
	public string LocalBitcoinCoreDataDir => GetEffectiveValue<StringValue, string>(nameof(LocalBitcoinCoreDataDir));
	public EndPoint MainNetBitcoinP2pEndPoint => GetEffectiveValue<EndPointValue, EndPoint>(nameof(MainNetBitcoinP2pEndPoint));
	public EndPoint TestNetBitcoinP2pEndPoint => GetEffectiveValue<EndPointValue, EndPoint>(nameof(TestNetBitcoinP2pEndPoint));
	public EndPoint RegTestBitcoinP2pEndPoint => GetEffectiveValue<EndPointValue, EndPoint>(nameof(RegTestBitcoinP2pEndPoint));
	public bool JsonRpcServerEnabled => GetEffectiveValue<BoolValue, bool>(nameof(JsonRpcServerEnabled));
	public string JsonRpcUser => GetEffectiveValue<StringValue, string>(nameof(JsonRpcUser));
	public string JsonRpcPassword => GetEffectiveValue<StringValue, string>(nameof(JsonRpcPassword));
	public string[] JsonRpcServerPrefixes => GetEffectiveValue<StringArrayValue, string[]>(nameof(JsonRpcServerPrefixes));
	public bool RpcOnionEnabled => GetEffectiveValue<BoolValue, bool>(nameof(RpcOnionEnabled));
	public Money DustThreshold => GetEffectiveValue<MoneyValue, Money>(nameof(DustThreshold));
	public bool BlockOnlyMode => GetEffectiveValue<BoolValue, bool>(nameof(BlockOnlyMode));
	public string LogLevel => GetEffectiveValue<StringValue, string>(nameof(LogLevel));

	public bool EnableGpu => GetEffectiveValue<BoolValue, bool>(nameof(EnableGpu));
	public string CoordinatorIdentifier => GetEffectiveValue<StringValue, string>(nameof(CoordinatorIdentifier));
	public ServiceConfiguration ServiceConfiguration { get; }

	public static string DataDir { get; } = GetStringValue(
		"datadir",
		EnvironmentHelpers.GetDataDir(Path.Combine("WalletWasabi", "Client")),
		Environment.GetCommandLineArgs()).EffectiveValue;

	public bool IsOverridden { get; }

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

	public Uri GetBackendUri()
	{
		if (Network == Network.Main)
		{
			return new Uri(MainNetBackendUri);
		}

		if (Network == Network.TestNet)
		{
			return new Uri(TestNetBackendUri);
		}

		if (Network == Network.RegTest)
		{
			return new Uri(RegTestBackendUri);
		}

		throw new NotSupportedNetworkException(Network);
	}

	public Uri GetCoordinatorUri()
	{
		var result = Network switch
		{
			{ } n when n == Network.Main => MainNetCoordinatorUri,
			{ } n when n == Network.TestNet => TestNetCoordinatorUri,
			{ } n when n == Network.RegTest => RegTestCoordinatorUri,
			_ => throw new NotSupportedNetworkException(Network)
		};

		return result is null ? GetBackendUri() : new Uri(result);
	}

	public IEnumerable<(string ParameterName, string Hint)> GetConfigOptionsMetadata() =>
		Data.Select(x => (x.Key, x.Value.Hint));

	private EndPointValue GetEndPointValue(string key, EndPoint value, string[] cliArgs)
	{
		if (GetOverrideValue(key, cliArgs, out string? overrideValue, out ValueSource? valueSource))
		{
			if (!EndPointParser.TryParse(overrideValue, 0, out var endpoint))
			{
				throw new ArgumentNullException(key, "Not a valid endpoint");
			}

			return new EndPointValue(value, endpoint, valueSource.Value);
		}

		return new EndPointValue(value, value, ValueSource.Disk);
	}

	private MoneyValue GetMoneyValue(string key, Money value, string[] cliArgs)
	{
		if (GetOverrideValue(key, cliArgs, out string? overrideValue, out ValueSource? valueSource))
		{
			if (!Money.TryParse(overrideValue, out var money))
			{
				throw new ArgumentNullException("DustThreshold", "Not a valid money");
			}

			return new MoneyValue(value, money, valueSource.Value);
		}

		return new MoneyValue(value, value, ValueSource.Disk);
	}

	private NetworkValue GetNetworkValue(string key, string value, string[] cliArgs)
	{
		StringValue stringValue = GetStringValue(key, value, cliArgs);

		return new NetworkValue(
			Value: Network.GetNetwork(stringValue.Value) ?? throw new ArgumentException("Network", $"Unknown network '{stringValue.Value}'"),
			EffectiveValue: Network.GetNetwork(stringValue.EffectiveValue) ?? throw new ArgumentException("Network", $"Unknown network '{stringValue.EffectiveValue}'"),
			stringValue.ValueSource);
	}

	private BoolValue GetBoolValue(string key, bool value, string[] cliArgs)
	{
		if (GetOverrideValue(key, cliArgs, out string? overrideValue, out ValueSource? valueSource))
		{
			if (!bool.TryParse(overrideValue, out bool argsBoolValue))
			{
				throw new ArgumentException("must be 'true' or 'false'.", key);
			}

			return new BoolValue(value, argsBoolValue, valueSource.Value);
		}

		return new BoolValue(value, value, ValueSource.Disk);
	}

	private IntValue GetLongValue(string key, int value, string[] cliArgs)
	{
		if (GetOverrideValue(key, cliArgs, out string? overrideValue, out ValueSource? valueSource))
		{
			if (!int.TryParse(overrideValue, out int argsLongValue))
			{
				throw new ArgumentException("must be a number.", key);
			}

			return new IntValue(value, argsLongValue, valueSource.Value);
		}

		return new IntValue(value, value, ValueSource.Disk);
	}

	private static StringValue GetStringValue(string key, string value, string[] cliArgs)
	{
		if (GetOverrideValue(key, cliArgs, out string? overrideValue, out ValueSource? valueSource))
		{
			return new StringValue(value, overrideValue, valueSource.Value);
		}

		return new StringValue(value, value, ValueSource.Disk);
	}

	private static NullableStringValue GetNullableStringValue(string key, string? value, string[] cliArgs)
	{
		if (GetOverrideValue(key, cliArgs, out string? overrideValue, out ValueSource? valueSource))
		{
			return new NullableStringValue(value, overrideValue, valueSource.Value);
		}

		return new NullableStringValue(value, value, ValueSource.Disk);
	}

	private static StringArrayValue GetStringArrayValue(string key, string[] arrayValues, string[] cliArgs)
	{
		if (GetOverrideValue(key, cliArgs, out string? overrideValue, out ValueSource? valueSource))
		{
			return new StringArrayValue(arrayValues, new string[] { overrideValue }, valueSource.Value);
		}

		return new StringArrayValue(arrayValues, arrayValues, ValueSource.Disk);
	}

	private static bool GetOverrideValue(string key, string[] cliArgs, [NotNullWhen(true)] out string? overrideValue, [NotNullWhen(true)] out ValueSource? valueSource)
	{
		// CLI arguments have higher precedence than environment variables.
		if (GetCliArgsValue(key, cliArgs, out string? argsValue))
		{
			valueSource = ValueSource.CommandLineArgument;
			overrideValue = argsValue;
			return true;
		}

		if (GetEnvironmentVariable(key, out string? envVarValue))
		{
			valueSource = ValueSource.EnvironmentVariable;
			overrideValue = envVarValue;
			return true;
		}

		valueSource = null;
		overrideValue = null;
		return false;
	}

	private static bool GetCliArgsValue(string key, string[] cliArgs, [NotNullWhen(true)] out string? cliArgsValue)
	{
		if (ArgumentHelpers.TryGetValue(key, cliArgs, out cliArgsValue))
		{
			return true;
		}

		cliArgsValue = null;
		return false;
	}

	private static bool GetEnvironmentVariable(string key, [NotNullWhen(true)] out string? envValue)
	{
		string envKey = $"WASABI-{key.ToUpperInvariant()}";

		if (EnvironmentVariables.Contains(envKey))
		{
			if (EnvironmentVariables[envKey] is string envVar)
			{
				envValue = envVar;
				return true;
			}
		}

		envValue = null;
		return false;
	}

	private TValue GetEffectiveValue<TStorage, TValue>(string key) where TStorage : ITypedValue<TValue>
	{
		if (Data.TryGetValue(key, out (string, IValue value) valueObject) && valueObject.value is ITypedValue<TValue> typedValue)
		{
			return typedValue.EffectiveValue;
		}

		throw new InvalidOperationException($"Failed to find key '{key}' in config storage.");
	}

	/// <summary>Source of application config value.</summary>
	private enum ValueSource
	{
		/// <summary>Value stored in JSON config on disk.</summary>
		Disk,

		/// <summary>CLI argument passed by user to override disk config value.</summary>
		CommandLineArgument,

		/// <summary>Environment variable overriding disk config value.</summary>
		EnvironmentVariable
	}

	private interface IValue
	{
		ValueSource ValueSource { get; }
		bool Overridden => ValueSource != ValueSource.Disk;
	}

	private interface ITypedValue<T> : IValue
	{
		T Value { get; }
		T EffectiveValue { get; }
	}

	private record BoolValue(bool Value, bool EffectiveValue, ValueSource ValueSource) : ITypedValue<bool>;
	private record IntValue(int Value, int EffectiveValue, ValueSource ValueSource) : ITypedValue<int>;
	private record StringValue(string Value, string EffectiveValue, ValueSource ValueSource) : ITypedValue<string>;
	private record NullableStringValue(string? Value, string? EffectiveValue, ValueSource ValueSource) : ITypedValue<string?>;
	private record StringArrayValue(string[] Value, string[] EffectiveValue, ValueSource ValueSource) : ITypedValue<string[]>;
	private record NetworkValue(Network Value, Network EffectiveValue, ValueSource ValueSource) : ITypedValue<Network>;
	private record MoneyValue(Money Value, Money EffectiveValue, ValueSource ValueSource) : ITypedValue<Money>;
	private record EndPointValue(EndPoint Value, EndPoint EffectiveValue, ValueSource ValueSource) : ITypedValue<EndPoint>;
}

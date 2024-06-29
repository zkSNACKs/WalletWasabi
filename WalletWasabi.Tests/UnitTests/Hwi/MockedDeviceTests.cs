using NBitcoin;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Exceptions;
using WalletWasabi.Helpers;
using WalletWasabi.Hwi;
using WalletWasabi.Hwi.Exceptions;
using WalletWasabi.Hwi.Models;
using WalletWasabi.Hwi.Parsers;
using WalletWasabi.Tests.Helpers;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.Hwi;

public class MockedDeviceTests
{
	#region SharedVariables

	public TimeSpan ReasonableRequestTimeout { get; } = TimeSpan.FromMinutes(3);

	#endregion SharedVariables

	#region Tests

	[Theory]
	[MemberData(nameof(GetDifferentNetworkValues))]
	public async Task TrezorTMockTestsAsync(Network network)
	{
		var client = new HwiClient(network, new HwiProcessBridgeMock(HardwareWalletModels.Trezor_T));

		using var cts = new CancellationTokenSource(ReasonableRequestTimeout);
		IEnumerable<HwiEnumerateEntry> enumerate = await client.EnumerateAsync(cts.Token);
		Assert.Single(enumerate);
		HwiEnumerateEntry entry = enumerate.Single();
		Assert.Equal(HardwareWalletModels.Trezor_T, entry.Model);
		Assert.Equal("webusb: 001:4", entry.Path);
		Assert.False(entry.NeedsPassphraseSent);
		Assert.False(entry.NeedsPinSent);
		Assert.NotNull(entry.Error);
		Assert.NotEmpty(entry.Error);
		Assert.Equal(HwiErrorCode.DeviceNotInitialized, entry.Code);
		Assert.False(entry.IsInitialized());
		Assert.Null(entry.Fingerprint);

		var deviceType = entry.Model;
		var devicePath = entry.Path;

		await client.WipeAsync(deviceType, devicePath, cts.Token);
		await client.SetupAsync(deviceType, devicePath, false, cts.Token);
		await client.RestoreAsync(deviceType, devicePath, false, cts.Token);

		// Trezor T doesn't support it.
		var promptpin = await Assert.ThrowsAsync<HwiException>(async () => await client.PromptPinAsync(deviceType, devicePath, cts.Token));
		Assert.Equal("The PIN has already been sent to this device", promptpin.Message);
		Assert.Equal(HwiErrorCode.DeviceAlreadyUnlocked, promptpin.ErrorCode);

		var sendpin = await Assert.ThrowsAsync<HwiException>(async () => await client.SendPinAsync(deviceType, devicePath, 1111, cts.Token));
		Assert.Equal("The PIN has already been sent to this device", sendpin.Message);
		Assert.Equal(HwiErrorCode.DeviceAlreadyUnlocked, sendpin.ErrorCode);

		KeyPath keyPath1 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit);
		KeyPath keyPath2 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive(1);
		ExtPubKey xpub1 = await client.GetXpubAsync(deviceType, devicePath, keyPath1, cts.Token);
		ExtPubKey xpub2 = await client.GetXpubAsync(deviceType, devicePath, keyPath2, cts.Token);
		ExtPubKey expectedXpub1;
		ExtPubKey expectedXpub2;
		if (network == Network.TestNet)
		{
			expectedXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6CaGC5LjEw1YWw8br7AURnB6ioJY2bEVApXh8NMsPQ9mdDbzN51iwVrnmGSof3MfjjRrntnE8mbYeTW5ywgvCXdjqF8meQEwnhPDQV2TW7c");
			expectedXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6E7pup6CRRS5jM1r3HVYQhHwQHpddJALjRDbsVDtsnQJozHrfE8Pua2X5JhtkWCxdcmGhPXWxV7DoJtSgZSUvUy6cvDchVQt2RGEd4mD4FA");
		}
		else
		{
			expectedXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6DHjDx4gzLV37gJWMxYJAqyKRGN46MT61RHVizdU62cbVUYu9L95cXKzX62yJ2hPbN11EeprS8sSn8kj47skQBrmycCMzFEYBQSntVKFQ5M");
			expectedXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6FJS1ne3STcKdQ9JLXNzZXidmCNZ9dxLiy7WVvsRkcmxjJsrDKJKEAXq4MGyEBM3vHEw2buqXezfNK5SNBrkwK7Fxjz1TW6xzRr2pUyMWFu");
		}
		Assert.Equal(expectedXpub1, xpub1);
		Assert.Equal(expectedXpub2, xpub2);

		BitcoinWitPubKeyAddress address1 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token);
		BitcoinWitPubKeyAddress address2 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath2, cts.Token);

		BitcoinAddress expectedAddress1;
		BitcoinAddress expectedAddress2;
		if (network == Network.Main)
		{
			expectedAddress1 = BitcoinAddress.Create("bc1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7fdevah", Network.Main);
			expectedAddress2 = BitcoinAddress.Create("bc1qmaveee425a5xjkjcv7m6d4gth45jvtnj23fzyf", Network.Main);
		}
		else if (network == Network.TestNet)
		{
			expectedAddress1 = BitcoinAddress.Create("tb1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7rtzlxy", Network.TestNet);
			expectedAddress2 = BitcoinAddress.Create("tb1qmaveee425a5xjkjcv7m6d4gth45jvtnjqhj3l6", Network.TestNet);
		}
		else if (network == Network.RegTest)
		{
			expectedAddress1 = BitcoinAddress.Create("bcrt1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7pzmj3d", Network.RegTest);
			expectedAddress2 = BitcoinAddress.Create("bcrt1qmaveee425a5xjkjcv7m6d4gth45jvtnjz7tugn", Network.RegTest);
		}
		else
		{
			throw new NotSupportedNetworkException(network);
		}

		Assert.Equal(expectedAddress1, address1);
		Assert.Equal(expectedAddress2, address2);
	}

	[Theory]
	[MemberData(nameof(GetDifferentNetworkValues))]
	public async Task TrezorSafe3MockTestsAsync(Network network)
	{
		var client = new HwiClient(network, new HwiProcessBridgeMock(HardwareWalletModels.Trezor_Safe_3));

		using var cts = new CancellationTokenSource(ReasonableRequestTimeout);
		IEnumerable<HwiEnumerateEntry> enumerate = await client.EnumerateAsync(cts.Token);
		Assert.Single(enumerate);
		HwiEnumerateEntry entry = enumerate.Single();
		Assert.Equal(HardwareWalletModels.Trezor_Safe_3, entry.Model);
		Assert.Equal("webusb: 001:9", entry.Path);
		Assert.False(entry.NeedsPassphraseSent);
		Assert.False(entry.NeedsPinSent);
		Assert.Null(entry.Error);
		Assert.Null(entry.Error);
		Assert.True(entry.IsInitialized());
		Assert.NotNull(entry.Fingerprint);

		var deviceType = entry.Model;
		var devicePath = entry.Path;

		await client.WipeAsync(deviceType, devicePath, cts.Token);
		await client.SetupAsync(deviceType, devicePath, false, cts.Token);
		await client.RestoreAsync(deviceType, devicePath, false, cts.Token);

		// Trezor Safe 3 doesn't support it.
		var promptpin = await Assert.ThrowsAsync<HwiException>(async () => await client.PromptPinAsync(deviceType, devicePath, cts.Token));
		Assert.Equal("The PIN has already been sent to this device", promptpin.Message);
		Assert.Equal(HwiErrorCode.DeviceAlreadyUnlocked, promptpin.ErrorCode);

		var sendpin = await Assert.ThrowsAsync<HwiException>(async () => await client.SendPinAsync(deviceType, devicePath, 1111, cts.Token));
		Assert.Equal("The PIN has already been sent to this device", sendpin.Message);
		Assert.Equal(HwiErrorCode.DeviceAlreadyUnlocked, sendpin.ErrorCode);

		KeyPath keyPath1 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit);
		KeyPath keyPath2 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive(1);
		ExtPubKey xpub1 = await client.GetXpubAsync(deviceType, devicePath, keyPath1, cts.Token);
		ExtPubKey xpub2 = await client.GetXpubAsync(deviceType, devicePath, keyPath2, cts.Token);
		ExtPubKey expectedXpub1;
		ExtPubKey expectedXpub2;
		if (network == Network.TestNet)
		{
			expectedXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6CaGC5LjEw1YWw8br7AURnB6ioJY2bEVApXh8NMsPQ9mdDbzN51iwVrnmGSof3MfjjRrntnE8mbYeTW5ywgvCXdjqF8meQEwnhPDQV2TW7c");
			expectedXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6E7pup6CRRS5jM1r3HVYQhHwQHpddJALjRDbsVDtsnQJozHrfE8Pua2X5JhtkWCxdcmGhPXWxV7DoJtSgZSUvUy6cvDchVQt2RGEd4mD4FA");
		}
		else
		{
			expectedXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6DHjDx4gzLV37gJWMxYJAqyKRGN46MT61RHVizdU62cbVUYu9L95cXKzX62yJ2hPbN11EeprS8sSn8kj47skQBrmycCMzFEYBQSntVKFQ5M");
			expectedXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6FJS1ne3STcKdQ9JLXNzZXidmCNZ9dxLiy7WVvsRkcmxjJsrDKJKEAXq4MGyEBM3vHEw2buqXezfNK5SNBrkwK7Fxjz1TW6xzRr2pUyMWFu");
		}
		Assert.Equal(expectedXpub1, xpub1);
		Assert.Equal(expectedXpub2, xpub2);

		BitcoinWitPubKeyAddress address1 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token);
		BitcoinWitPubKeyAddress address2 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath2, cts.Token);

		BitcoinAddress expectedAddress1;
		BitcoinAddress expectedAddress2;
		if (network == Network.Main)
		{
			expectedAddress1 = BitcoinAddress.Create("bc1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7fdevah", Network.Main);
			expectedAddress2 = BitcoinAddress.Create("bc1qmaveee425a5xjkjcv7m6d4gth45jvtnj23fzyf", Network.Main);
		}
		else if (network == Network.TestNet)
		{
			expectedAddress1 = BitcoinAddress.Create("tb1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7rtzlxy", Network.TestNet);
			expectedAddress2 = BitcoinAddress.Create("tb1qmaveee425a5xjkjcv7m6d4gth45jvtnjqhj3l6", Network.TestNet);
		}
		else if (network == Network.RegTest)
		{
			expectedAddress1 = BitcoinAddress.Create("bcrt1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7pzmj3d", Network.RegTest);
			expectedAddress2 = BitcoinAddress.Create("bcrt1qmaveee425a5xjkjcv7m6d4gth45jvtnjz7tugn", Network.RegTest);
		}
		else
		{
			throw new NotSupportedNetworkException(network);
		}

		Assert.Equal(expectedAddress1, address1);
		Assert.Equal(expectedAddress2, address2);
	}

	[Theory]
	[MemberData(nameof(GetDifferentNetworkValues))]
	public async Task TrezorOneMockTestsAsync(Network network)
	{
		var client = new HwiClient(network, new HwiProcessBridgeMock(HardwareWalletModels.Trezor_1));

		using var cts = new CancellationTokenSource(ReasonableRequestTimeout);
		IEnumerable<HwiEnumerateEntry> enumerate = await client.EnumerateAsync(cts.Token);
		Assert.Single(enumerate);
		HwiEnumerateEntry entry = enumerate.Single();
		Assert.Equal(HardwareWalletModels.Trezor_1, entry.Model);
		string rawPath = "hid:\\\\\\\\?\\\\hid#vid_534c&pid_0001&mi_00#7&6f0b727&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
		string normalizedPath = HwiParser.NormalizeRawDevicePath(rawPath);
		Assert.Equal(normalizedPath, entry.Path);
		Assert.False(entry.NeedsPassphraseSent);
		Assert.True(entry.NeedsPinSent);
		Assert.Equal("Could not open client or get fingerprint information: Trezor is locked. Unlock by using 'promptpin' and then 'sendpin'.", entry.Error);
		Assert.Equal(HwiErrorCode.DeviceNotReady, entry.Code);
		Assert.Null(entry.Fingerprint);
		Assert.True(entry.IsInitialized());

		var deviceType = entry.Model;
		var devicePath = entry.Path;

		await client.WipeAsync(deviceType, devicePath, cts.Token);
		await client.SetupAsync(deviceType, devicePath, false, cts.Token);
		await client.RestoreAsync(deviceType, devicePath, false, cts.Token);

		var promptpin = await Assert.ThrowsAsync<HwiException>(async () => await client.PromptPinAsync(deviceType, devicePath, cts.Token));
		Assert.Equal("The PIN has already been sent to this device", promptpin.Message);
		Assert.Equal(HwiErrorCode.DeviceAlreadyUnlocked, promptpin.ErrorCode);

		var sendpin = await Assert.ThrowsAsync<HwiException>(async () => await client.SendPinAsync(deviceType, devicePath, 1111, cts.Token));
		Assert.Equal("The PIN has already been sent to this device", sendpin.Message);
		Assert.Equal(HwiErrorCode.DeviceAlreadyUnlocked, sendpin.ErrorCode);

		KeyPath keyPath1 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit);
		KeyPath keyPath2 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive(1);
		ExtPubKey xpub1 = await client.GetXpubAsync(deviceType, devicePath, keyPath1, cts.Token);
		ExtPubKey xpub2 = await client.GetXpubAsync(deviceType, devicePath, keyPath2, cts.Token);
		ExtPubKey expectedXpub1;
		ExtPubKey expectedXpub2;
		if (network == Network.TestNet)
		{
			expectedXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6CaGC5LjEw1YWw8br7AURnB6ioJY2bEVApXh8NMsPQ9mdDbzN51iwVrnmGSof3MfjjRrntnE8mbYeTW5ywgvCXdjqF8meQEwnhPDQV2TW7c");
			expectedXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6E7pup6CRRS5jM1r3HVYQhHwQHpddJALjRDbsVDtsnQJozHrfE8Pua2X5JhtkWCxdcmGhPXWxV7DoJtSgZSUvUy6cvDchVQt2RGEd4mD4FA");
		}
		else
		{
			expectedXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6DHjDx4gzLV37gJWMxYJAqyKRGN46MT61RHVizdU62cbVUYu9L95cXKzX62yJ2hPbN11EeprS8sSn8kj47skQBrmycCMzFEYBQSntVKFQ5M");
			expectedXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6FJS1ne3STcKdQ9JLXNzZXidmCNZ9dxLiy7WVvsRkcmxjJsrDKJKEAXq4MGyEBM3vHEw2buqXezfNK5SNBrkwK7Fxjz1TW6xzRr2pUyMWFu");
		}
		Assert.Equal(expectedXpub1, xpub1);
		Assert.Equal(expectedXpub2, xpub2);

		BitcoinWitPubKeyAddress address1 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token);
		BitcoinWitPubKeyAddress address2 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath2, cts.Token);

		BitcoinAddress expectedAddress1;
		BitcoinAddress expectedAddress2;
		if (network == Network.Main)
		{
			expectedAddress1 = BitcoinAddress.Create("bc1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7fdevah", Network.Main);
			expectedAddress2 = BitcoinAddress.Create("bc1qmaveee425a5xjkjcv7m6d4gth45jvtnj23fzyf", Network.Main);
		}
		else if (network == Network.TestNet)
		{
			expectedAddress1 = BitcoinAddress.Create("tb1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7rtzlxy", Network.TestNet);
			expectedAddress2 = BitcoinAddress.Create("tb1qmaveee425a5xjkjcv7m6d4gth45jvtnjqhj3l6", Network.TestNet);
		}
		else if (network == Network.RegTest)
		{
			expectedAddress1 = BitcoinAddress.Create("bcrt1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7pzmj3d", Network.RegTest);
			expectedAddress2 = BitcoinAddress.Create("bcrt1qmaveee425a5xjkjcv7m6d4gth45jvtnjz7tugn", Network.RegTest);
		}
		else
		{
			throw new NotSupportedNetworkException(network);
		}

		Assert.Equal(expectedAddress1, address1);
		Assert.Equal(expectedAddress2, address2);
	}

	[Theory]
	[MemberData(nameof(GetDifferentNetworkValues))]
	public async Task ColdCardMk1MockTestsAsync(Network network)
	{
		var client = new HwiClient(network, new HwiProcessBridgeMock(HardwareWalletModels.Coldcard));

		using var cts = new CancellationTokenSource(ReasonableRequestTimeout);
		IEnumerable<HwiEnumerateEntry> enumerate = await client.EnumerateAsync(cts.Token);
		Assert.Single(enumerate);
		HwiEnumerateEntry entry = enumerate.Single();
		Assert.Equal(HardwareWalletModels.Coldcard, entry.Model);
		string rawPath = "\\\\\\\\?\\\\hid#vid_d13e&pid_cc10&mi_00#7&1b239988&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
		string normalizedPath = HwiParser.NormalizeRawDevicePath(rawPath);
		Assert.Equal(normalizedPath, entry.Path);
		Assert.Null(entry.NeedsPassphraseSent);
		Assert.Null(entry.NeedsPinSent);
		Assert.Null(entry.Error);
		Assert.Null(entry.Code);
		Assert.Equal("a3d0d797", entry.Fingerprint.ToString());
		Assert.True(entry.IsInitialized());

		var deviceType = entry.Model;
		var devicePath = entry.Path;

		var wipe = await Assert.ThrowsAsync<HwiException>(async () => await client.WipeAsync(deviceType, devicePath, cts.Token));
		Assert.Equal("The Coldcard does not support wiping via software", wipe.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, wipe.ErrorCode);

		// ColdCard doesn't support it.
		var setup = await Assert.ThrowsAsync<HwiException>(async () => await client.SetupAsync(deviceType, devicePath, false, cts.Token));
		Assert.Equal("The Coldcard does not support software setup", setup.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, setup.ErrorCode);

		// ColdCard doesn't support it.
		var restore = await Assert.ThrowsAsync<HwiException>(async () => await client.RestoreAsync(deviceType, devicePath, false, cts.Token));
		Assert.Equal("The Coldcard does not support restoring via software", restore.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, restore.ErrorCode);

		// ColdCard doesn't support it.
		var promptpin = await Assert.ThrowsAsync<HwiException>(async () => await client.PromptPinAsync(deviceType, devicePath, cts.Token));
		Assert.Equal("The Coldcard does not need a PIN sent from the host", promptpin.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, promptpin.ErrorCode);

		// ColdCard doesn't support it.
		var sendpin = await Assert.ThrowsAsync<HwiException>(async () => await client.SendPinAsync(deviceType, devicePath, 1111, cts.Token));
		Assert.Equal("The Coldcard does not need a PIN sent from the host", sendpin.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, sendpin.ErrorCode);

		KeyPath keyPath1 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit);
		KeyPath keyPath2 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive(1);
		ExtPubKey xpub1 = await client.GetXpubAsync(deviceType, devicePath, keyPath1, cts.Token);
		ExtPubKey xpub2 = await client.GetXpubAsync(deviceType, devicePath, keyPath2, cts.Token);
		ExtPubKey expectedXpub1;
		ExtPubKey expectedXpub2;
		if (network == Network.TestNet)
		{
			expectedXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6CaGC5LjEw1YWw8br7AURnB6ioJY2bEVApXh8NMsPQ9mdDbzN51iwVrnmGSof3MfjjRrntnE8mbYeTW5ywgvCXdjqF8meQEwnhPDQV2TW7c");
			expectedXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6E7pup6CRRS5jM1r3HVYQhHwQHpddJALjRDbsVDtsnQJozHrfE8Pua2X5JhtkWCxdcmGhPXWxV7DoJtSgZSUvUy6cvDchVQt2RGEd4mD4FA");
		}
		else
		{
			expectedXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6DHjDx4gzLV37gJWMxYJAqyKRGN46MT61RHVizdU62cbVUYu9L95cXKzX62yJ2hPbN11EeprS8sSn8kj47skQBrmycCMzFEYBQSntVKFQ5M");
			expectedXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6FJS1ne3STcKdQ9JLXNzZXidmCNZ9dxLiy7WVvsRkcmxjJsrDKJKEAXq4MGyEBM3vHEw2buqXezfNK5SNBrkwK7Fxjz1TW6xzRr2pUyMWFu");
		}
		Assert.Equal(expectedXpub1, xpub1);
		Assert.Equal(expectedXpub2, xpub2);

		BitcoinWitPubKeyAddress address1 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token);
		BitcoinWitPubKeyAddress address2 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath2, cts.Token);

		BitcoinAddress expectedAddress1;
		BitcoinAddress expectedAddress2;
		if (network == Network.Main)
		{
			expectedAddress1 = BitcoinAddress.Create("bc1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7fdevah", Network.Main);
			expectedAddress2 = BitcoinAddress.Create("bc1qmaveee425a5xjkjcv7m6d4gth45jvtnj23fzyf", Network.Main);
		}
		else if (network == Network.TestNet)
		{
			expectedAddress1 = BitcoinAddress.Create("tb1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7rtzlxy", Network.TestNet);
			expectedAddress2 = BitcoinAddress.Create("tb1qmaveee425a5xjkjcv7m6d4gth45jvtnjqhj3l6", Network.TestNet);
		}
		else if (network == Network.RegTest)
		{
			expectedAddress1 = BitcoinAddress.Create("bcrt1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7pzmj3d", Network.RegTest);
			expectedAddress2 = BitcoinAddress.Create("bcrt1qmaveee425a5xjkjcv7m6d4gth45jvtnjz7tugn", Network.RegTest);
		}
		else
		{
			throw new NotSupportedNetworkException(network);
		}

		Assert.Equal(expectedAddress1, address1);
		Assert.Equal(expectedAddress2, address2);
	}

	[Theory]
	[MemberData(nameof(GetDifferentNetworkValues))]
	public async Task LedgerNanoSTestsAsync(Network network)
	{
		var client = new HwiClient(network, new HwiProcessBridgeMock(HardwareWalletModels.Ledger_Nano_S));

		using var cts = new CancellationTokenSource(ReasonableRequestTimeout);
		IEnumerable<HwiEnumerateEntry> enumerate = await client.EnumerateAsync(cts.Token);
		Assert.Single(enumerate);
		HwiEnumerateEntry entry = enumerate.Single();
		Assert.Equal(HardwareWalletModels.Ledger_Nano_S, entry.Model);
		Assert.Equal(@"\\?\hid#vid_2c97&pid_0001&mi_00#7&e45ae20&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", entry.Path);
		Assert.False(entry.NeedsPassphraseSent);
		Assert.False(entry.NeedsPinSent);
		Assert.Null(entry.Error);
		Assert.Null(entry.Code);
		Assert.True(entry.IsInitialized());
		Assert.Equal("4054d6f6", entry.Fingerprint.ToString());

		var deviceType = entry.Model;
		var devicePath = entry.Path;

		var wipe = await Assert.ThrowsAsync<HwiException>(async () => await client.WipeAsync(deviceType, devicePath, cts.Token));
		Assert.Equal("The Ledger Nano S does not support wiping via software", wipe.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, wipe.ErrorCode);

		var setup = await Assert.ThrowsAsync<HwiException>(async () => await client.SetupAsync(deviceType, devicePath, false, cts.Token));
		Assert.Equal("The Ledger Nano S does not support software setup", setup.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, setup.ErrorCode);

		var restore = await Assert.ThrowsAsync<HwiException>(async () => await client.RestoreAsync(deviceType, devicePath, false, cts.Token));
		Assert.Equal("The Ledger Nano S does not support restoring via software", restore.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, restore.ErrorCode);

		var promptpin = await Assert.ThrowsAsync<HwiException>(async () => await client.PromptPinAsync(deviceType, devicePath, cts.Token));
		Assert.Equal("The Ledger Nano S does not need a PIN sent from the host", promptpin.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, promptpin.ErrorCode);

		var sendpin = await Assert.ThrowsAsync<HwiException>(async () => await client.SendPinAsync(deviceType, devicePath, 1111, cts.Token));
		Assert.Equal("The Ledger Nano S does not need a PIN sent from the host", sendpin.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, sendpin.ErrorCode);

		KeyPath keyPath1 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit);
		KeyPath keyPath2 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive(1);
		ExtPubKey xpub1 = await client.GetXpubAsync(deviceType, devicePath, keyPath1, cts.Token);
		ExtPubKey xpub2 = await client.GetXpubAsync(deviceType, devicePath, keyPath2, cts.Token);
		ExtPubKey expectedXpub1;
		ExtPubKey expectedXpub2;
		if (network == Network.TestNet)
		{
			expectedXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6CaGC5LjEw1YWw8br7AURnB6ioJY2bEVApXh8NMsPQ9mdDbzN51iwVrnmGSof3MfjjRrntnE8mbYeTW5ywgvCXdjqF8meQEwnhPDQV2TW7c");
			expectedXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6E7pup6CRRS5jM1r3HVYQhHwQHpddJALjRDbsVDtsnQJozHrfE8Pua2X5JhtkWCxdcmGhPXWxV7DoJtSgZSUvUy6cvDchVQt2RGEd4mD4FA");
		}
		else
		{
			expectedXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6DHjDx4gzLV37gJWMxYJAqyKRGN46MT61RHVizdU62cbVUYu9L95cXKzX62yJ2hPbN11EeprS8sSn8kj47skQBrmycCMzFEYBQSntVKFQ5M");
			expectedXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6FJS1ne3STcKdQ9JLXNzZXidmCNZ9dxLiy7WVvsRkcmxjJsrDKJKEAXq4MGyEBM3vHEw2buqXezfNK5SNBrkwK7Fxjz1TW6xzRr2pUyMWFu");
		}
		Assert.Equal(expectedXpub1, xpub1);
		Assert.Equal(expectedXpub2, xpub2);

		BitcoinWitPubKeyAddress address1 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token);
		BitcoinWitPubKeyAddress address2 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath2, cts.Token);

		BitcoinAddress expectedAddress1;
		BitcoinAddress expectedAddress2;
		if (network == Network.Main)
		{
			expectedAddress1 = BitcoinAddress.Create("bc1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7fdevah", Network.Main);
			expectedAddress2 = BitcoinAddress.Create("bc1qmaveee425a5xjkjcv7m6d4gth45jvtnj23fzyf", Network.Main);
		}
		else if (network == Network.TestNet)
		{
			expectedAddress1 = BitcoinAddress.Create("tb1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7rtzlxy", Network.TestNet);
			expectedAddress2 = BitcoinAddress.Create("tb1qmaveee425a5xjkjcv7m6d4gth45jvtnjqhj3l6", Network.TestNet);
		}
		else if (network == Network.RegTest)
		{
			expectedAddress1 = BitcoinAddress.Create("bcrt1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7pzmj3d", Network.RegTest);
			expectedAddress2 = BitcoinAddress.Create("bcrt1qmaveee425a5xjkjcv7m6d4gth45jvtnjz7tugn", Network.RegTest);
		}
		else
		{
			throw new NotSupportedNetworkException(network);
		}

		Assert.Equal(expectedAddress1, address1);
		Assert.Equal(expectedAddress2, address2);
	}

	[Theory]
	[MemberData(nameof(GetDifferentNetworkValues))]
	public async Task LedgerNanoXTestsAsync(Network network)
	{
		var client = new HwiClient(network, new HwiProcessBridgeMock(HardwareWalletModels.Ledger_Nano_X));

		using var cts = new CancellationTokenSource(ReasonableRequestTimeout);
		IEnumerable<HwiEnumerateEntry> enumerate = await client.EnumerateAsync(cts.Token);
		Assert.Single(enumerate);
		HwiEnumerateEntry entry = enumerate.Single();
		Assert.Equal(HardwareWalletModels.Ledger_Nano_X, entry.Model);
		Assert.Equal(@"\\?\hid#vid_2c97&pid_0001&mi_00#7&e45ae20&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", entry.Path);
		Assert.False(entry.NeedsPassphraseSent);
		Assert.False(entry.NeedsPinSent);
		Assert.Null(entry.Error);
		Assert.Null(entry.Code);
		Assert.True(entry.IsInitialized());
		Assert.Equal("4054d6f6", entry.Fingerprint.ToString());

		var deviceType = entry.Model;
		var devicePath = entry.Path;

		var wipe = await Assert.ThrowsAsync<HwiException>(async () => await client.WipeAsync(deviceType, devicePath, cts.Token));
		Assert.Equal("The Ledger Nano X does not support wiping via software", wipe.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, wipe.ErrorCode);

		var setup = await Assert.ThrowsAsync<HwiException>(async () => await client.SetupAsync(deviceType, devicePath, false, cts.Token));
		Assert.Equal("The Ledger Nano X does not support software setup", setup.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, setup.ErrorCode);

		var restore = await Assert.ThrowsAsync<HwiException>(async () => await client.RestoreAsync(deviceType, devicePath, false, cts.Token));
		Assert.Equal("The Ledger Nano X does not support restoring via software", restore.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, restore.ErrorCode);

		var promptpin = await Assert.ThrowsAsync<HwiException>(async () => await client.PromptPinAsync(deviceType, devicePath, cts.Token));
		Assert.Equal("The Ledger Nano X does not need a PIN sent from the host", promptpin.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, promptpin.ErrorCode);

		var sendpin = await Assert.ThrowsAsync<HwiException>(async () => await client.SendPinAsync(deviceType, devicePath, 1111, cts.Token));
		Assert.Equal("The Ledger Nano X does not need a PIN sent from the host", sendpin.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, sendpin.ErrorCode);

		KeyPath keyPath1 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit);
		KeyPath keyPath2 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive(1);
		ExtPubKey xpub1 = await client.GetXpubAsync(deviceType, devicePath, keyPath1, cts.Token);
		ExtPubKey xpub2 = await client.GetXpubAsync(deviceType, devicePath, keyPath2, cts.Token);
		ExtPubKey expectedXpub1;
		ExtPubKey expectedXpub2;
		if (network == Network.TestNet)
		{
			expectedXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6CaGC5LjEw1YWw8br7AURnB6ioJY2bEVApXh8NMsPQ9mdDbzN51iwVrnmGSof3MfjjRrntnE8mbYeTW5ywgvCXdjqF8meQEwnhPDQV2TW7c");
			expectedXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6E7pup6CRRS5jM1r3HVYQhHwQHpddJALjRDbsVDtsnQJozHrfE8Pua2X5JhtkWCxdcmGhPXWxV7DoJtSgZSUvUy6cvDchVQt2RGEd4mD4FA");
		}
		else
		{
			expectedXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6DHjDx4gzLV37gJWMxYJAqyKRGN46MT61RHVizdU62cbVUYu9L95cXKzX62yJ2hPbN11EeprS8sSn8kj47skQBrmycCMzFEYBQSntVKFQ5M");
			expectedXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6FJS1ne3STcKdQ9JLXNzZXidmCNZ9dxLiy7WVvsRkcmxjJsrDKJKEAXq4MGyEBM3vHEw2buqXezfNK5SNBrkwK7Fxjz1TW6xzRr2pUyMWFu");
		}
		Assert.Equal(expectedXpub1, xpub1);
		Assert.Equal(expectedXpub2, xpub2);

		BitcoinWitPubKeyAddress address1 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token);
		BitcoinWitPubKeyAddress address2 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath2, cts.Token);

		BitcoinAddress expectedAddress1;
		BitcoinAddress expectedAddress2;
		if (network == Network.Main)
		{
			expectedAddress1 = BitcoinAddress.Create("bc1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7fdevah", Network.Main);
			expectedAddress2 = BitcoinAddress.Create("bc1qmaveee425a5xjkjcv7m6d4gth45jvtnj23fzyf", Network.Main);
		}
		else if (network == Network.TestNet)
		{
			expectedAddress1 = BitcoinAddress.Create("tb1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7rtzlxy", Network.TestNet);
			expectedAddress2 = BitcoinAddress.Create("tb1qmaveee425a5xjkjcv7m6d4gth45jvtnjqhj3l6", Network.TestNet);
		}
		else if (network == Network.RegTest)
		{
			expectedAddress1 = BitcoinAddress.Create("bcrt1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7pzmj3d", Network.RegTest);
			expectedAddress2 = BitcoinAddress.Create("bcrt1qmaveee425a5xjkjcv7m6d4gth45jvtnjz7tugn", Network.RegTest);
		}
		else
		{
			throw new NotSupportedNetworkException(network);
		}

		Assert.Equal(expectedAddress1, address1);
		Assert.Equal(expectedAddress2, address2);
	}

	[Theory]
	[MemberData(nameof(GetDifferentNetworkValues))]
	public async Task JadeMockTestsAsync(Network network)
	{
		var client = new HwiClient(network, new HwiProcessBridgeMock(HardwareWalletModels.Jade));

		using var cts = new CancellationTokenSource(ReasonableRequestTimeout);
		IEnumerable<HwiEnumerateEntry> enumerate = await client.EnumerateAsync(cts.Token);
		HwiEnumerateEntry entry = Assert.Single(enumerate);
		Assert.Equal(HardwareWalletModels.Jade, entry.Model);
		Assert.True(HwiValidationHelper.ValidatePathString(entry.Model, "COM3", OSPlatform.Windows));
		Assert.Equal("COM3", entry.Path);
		Assert.False(entry.NeedsPassphraseSent);
		Assert.False(entry.NeedsPinSent);
		Assert.Null(entry.Error);
		Assert.Null(entry.Code);
		Assert.True(entry.IsInitialized());
		Assert.Equal("9bdca818", entry.Fingerprint.ToString());

		var deviceType = entry.Model;
		var devicePath = entry.Path;

		var wipe = await Assert.ThrowsAsync<HwiException>(async () => await client.WipeAsync(deviceType, devicePath, cts.Token));
		Assert.Equal("Blockstream Jade does not support wiping via software", wipe.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, wipe.ErrorCode);

		var setup = await Assert.ThrowsAsync<HwiException>(async () => await client.SetupAsync(deviceType, devicePath, false, cts.Token));
		Assert.Equal("Blockstream Jade does not support software setup", setup.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, setup.ErrorCode);

		var restore = await Assert.ThrowsAsync<HwiException>(async () => await client.RestoreAsync(deviceType, devicePath, false, cts.Token));
		Assert.Equal("Blockstream Jade does not support restoring via software", restore.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, restore.ErrorCode);

		var promptPinEx = await Assert.ThrowsAsync<HwiException>(async () => await client.PromptPinAsync(deviceType, devicePath, cts.Token));
		Assert.Equal("Blockstream Jade does not need a PIN sent from the host", promptPinEx.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, promptPinEx.ErrorCode);

		var sendPinEx = await Assert.ThrowsAsync<HwiException>(async () => await client.SendPinAsync(deviceType, devicePath, 1111, cts.Token));
		Assert.Equal("Blockstream Jade does not need a PIN sent from the host", sendPinEx.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, sendPinEx.ErrorCode);

		KeyPath keyPath1 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit);
		KeyPath keyPath2 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive(1);
		ExtPubKey xpub1 = await client.GetXpubAsync(deviceType, devicePath, keyPath1, cts.Token);
		ExtPubKey xpub2 = await client.GetXpubAsync(deviceType, devicePath, keyPath2, cts.Token);
		ExtPubKey expectedXpub1;
		ExtPubKey expectedXpub2;

		if (network == Network.TestNet)
		{
			expectedXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6CaGC5LjEw1YWw8br7AURnB6ioJY2bEVApXh8NMsPQ9mdDbzN51iwVrnmGSof3MfjjRrntnE8mbYeTW5ywgvCXdjqF8meQEwnhPDQV2TW7c");
			expectedXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6E7pup6CRRS5jM1r3HVYQhHwQHpddJALjRDbsVDtsnQJozHrfE8Pua2X5JhtkWCxdcmGhPXWxV7DoJtSgZSUvUy6cvDchVQt2RGEd4mD4FA");
		}
		else
		{
			expectedXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6DHjDx4gzLV37gJWMxYJAqyKRGN46MT61RHVizdU62cbVUYu9L95cXKzX62yJ2hPbN11EeprS8sSn8kj47skQBrmycCMzFEYBQSntVKFQ5M");
			expectedXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6FJS1ne3STcKdQ9JLXNzZXidmCNZ9dxLiy7WVvsRkcmxjJsrDKJKEAXq4MGyEBM3vHEw2buqXezfNK5SNBrkwK7Fxjz1TW6xzRr2pUyMWFu");
		}
		Assert.Equal(expectedXpub1, xpub1);
		Assert.Equal(expectedXpub2, xpub2);

		BitcoinWitPubKeyAddress address1 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token);
		BitcoinWitPubKeyAddress address2 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath2, cts.Token);

		BitcoinAddress expectedAddress1;
		BitcoinAddress expectedAddress2;
		if (network == Network.Main)
		{
			expectedAddress1 = BitcoinAddress.Create("bc1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7fdevah", network);
			expectedAddress2 = BitcoinAddress.Create("bc1qmaveee425a5xjkjcv7m6d4gth45jvtnj23fzyf", network);
		}
		else if (network == Network.TestNet)
		{
			expectedAddress1 = BitcoinAddress.Create("tb1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7rtzlxy", network);
			expectedAddress2 = BitcoinAddress.Create("tb1qmaveee425a5xjkjcv7m6d4gth45jvtnjqhj3l6", network);
		}
		else if (network == Network.RegTest)
		{
			expectedAddress1 = BitcoinAddress.Create("bcrt1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7pzmj3d", network);
			expectedAddress2 = BitcoinAddress.Create("bcrt1qmaveee425a5xjkjcv7m6d4gth45jvtnjz7tugn", network);
		}
		else
		{
			throw new NotSupportedNetworkException(network);
		}

		Assert.Equal(expectedAddress1, address1);
		Assert.Equal(expectedAddress2, address2);
	}

	[Theory]
	[MemberData(nameof(GetDifferentNetworkValues))]
	public async Task BitBox02BtcOnlyMockTestsAsync(Network network)
	{
		var client = new HwiClient(network, new HwiProcessBridgeMock(HardwareWalletModels.BitBox02_BTCOnly));

		using var cts = new CancellationTokenSource(ReasonableRequestTimeout);
		IEnumerable<HwiEnumerateEntry> enumerate = await client.EnumerateAsync(cts.Token);
		Assert.Single(enumerate);
		HwiEnumerateEntry entry = enumerate.Single();
		Assert.Equal(HardwareWalletModels.BitBox02_BTCOnly, entry.Model);
		Assert.True(HwiValidationHelper.ValidatePathString(entry.Model, @"\\?\hid#vid_03eb&pid_2403#6&229ae20&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", OSPlatform.Windows));
		Assert.Equal(@"\\?\hid#vid_03eb&pid_2403#6&229ae20&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", entry.Path);
		Assert.False(entry.NeedsPassphraseSent);
		Assert.False(entry.NeedsPinSent);
		Assert.Null(entry.Error);
		Assert.Null(entry.Code);
		Assert.True(entry.IsInitialized());
		Assert.NotNull(entry.Fingerprint);

		var deviceType = entry.Model;
		var devicePath = entry.Path;

		await client.WipeAsync(deviceType, devicePath, cts.Token);
		await client.SetupAsync(deviceType, devicePath, false, cts.Token);
		await client.RestoreAsync(deviceType, devicePath, false, cts.Token);

		// BitBox02 doesn't support it.
		var promptpin = await Assert.ThrowsAsync<HwiException>(async () => await client.PromptPinAsync(deviceType, devicePath, cts.Token));
		Assert.Equal("The BitBox02 does not need a PIN sent from the host", promptpin.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, promptpin.ErrorCode);

		var sendpin = await Assert.ThrowsAsync<HwiException>(async () => await client.SendPinAsync(deviceType, devicePath, 1111, cts.Token));
		Assert.Equal("The BitBox02 does not need a PIN sent from the host", sendpin.Message);
		Assert.Equal(HwiErrorCode.UnavailableAction, sendpin.ErrorCode);

		KeyPath keyPath1 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit);
		KeyPath keyPath2 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive(1);
		ExtPubKey xpub1 = await client.GetXpubAsync(deviceType, devicePath, keyPath1, cts.Token);
		ExtPubKey xpub2 = await client.GetXpubAsync(deviceType, devicePath, keyPath2, cts.Token);
		ExtPubKey expectedXpub1;
		ExtPubKey expectedXpub2;
		if (network == Network.TestNet)
		{
			expectedXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6CaGC5LjEw1YWw8br7AURnB6ioJY2bEVApXh8NMsPQ9mdDbzN51iwVrnmGSof3MfjjRrntnE8mbYeTW5ywgvCXdjqF8meQEwnhPDQV2TW7c");
			expectedXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6E7pup6CRRS5jM1r3HVYQhHwQHpddJALjRDbsVDtsnQJozHrfE8Pua2X5JhtkWCxdcmGhPXWxV7DoJtSgZSUvUy6cvDchVQt2RGEd4mD4FA");
		}
		else
		{
			expectedXpub1 = NBitcoinHelpers.BetterParseExtPubKey("xpub6DHjDx4gzLV37gJWMxYJAqyKRGN46MT61RHVizdU62cbVUYu9L95cXKzX62yJ2hPbN11EeprS8sSn8kj47skQBrmycCMzFEYBQSntVKFQ5M");
			expectedXpub2 = NBitcoinHelpers.BetterParseExtPubKey("xpub6FJS1ne3STcKdQ9JLXNzZXidmCNZ9dxLiy7WVvsRkcmxjJsrDKJKEAXq4MGyEBM3vHEw2buqXezfNK5SNBrkwK7Fxjz1TW6xzRr2pUyMWFu");
		}
		Assert.Equal(expectedXpub1, xpub1);
		Assert.Equal(expectedXpub2, xpub2);

		BitcoinWitPubKeyAddress address1 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token);
		BitcoinWitPubKeyAddress address2 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath2, cts.Token);

		BitcoinAddress expectedAddress1;
		BitcoinAddress expectedAddress2;
		if (network == Network.Main)
		{
			expectedAddress1 = BitcoinAddress.Create("bc1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7fdevah", Network.Main);
			expectedAddress2 = BitcoinAddress.Create("bc1qmaveee425a5xjkjcv7m6d4gth45jvtnj23fzyf", Network.Main);
		}
		else if (network == Network.TestNet)
		{
			expectedAddress1 = BitcoinAddress.Create("tb1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7rtzlxy", Network.TestNet);
			expectedAddress2 = BitcoinAddress.Create("tb1qmaveee425a5xjkjcv7m6d4gth45jvtnjqhj3l6", Network.TestNet);
		}
		else if (network == Network.RegTest)
		{
			expectedAddress1 = BitcoinAddress.Create("bcrt1q7zqqsmqx5ymhd7qn73lm96w5yqdkrmx7pzmj3d", Network.RegTest);
			expectedAddress2 = BitcoinAddress.Create("bcrt1qmaveee425a5xjkjcv7m6d4gth45jvtnjz7tugn", Network.RegTest);
		}
		else
		{
			throw new NotSupportedNetworkException(network);
		}

		Assert.Equal(expectedAddress1, address1);
		Assert.Equal(expectedAddress2, address2);
	}

	#endregion Tests

	#region HelperMethods

	public static IEnumerable<object[]> GetDifferentNetworkValues()
	{
		var networks = new List<Network>
			{
				Network.Main,
				Network.TestNet,
				Network.RegTest
			};

		foreach (Network network in networks)
		{
			yield return new object[] { network };
		}
	}

	#endregion HelperMethods
}

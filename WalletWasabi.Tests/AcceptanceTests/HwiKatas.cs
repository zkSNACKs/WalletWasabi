using System.IO;
using NBitcoin;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Hwi;
using WalletWasabi.Hwi.Exceptions;
using WalletWasabi.Hwi.Models;
using WalletWasabi.Hwi.Parsers;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.Tests.UnitTests.Helpers;
using Xunit;
using Microsoft.Extensions.Options;
using WalletWasabi.Fluent.Models;
using Moq;
using NBitcoin.Protocol;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Helpers;
using WalletWasabi.Models;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using WalletWasabi.Tests.RegressionTests;
using WalletWasabi.Tests.XunitConfiguration;
using WalletWasabi.Wallets;
using WalletWasabi.WebClients.Wasabi;
using Microsoft.Extensions.Caching.Memory;

namespace WalletWasabi.Tests.AcceptanceTests;

/// <summary>
/// Kata tests are intended to be run one by one.
/// A kata is a type of test that requires user interaction.
/// User interaction shall be defined in the beginning of the each kata.
/// Only write katas those require button push responses (eg. don't call setup on trezor.)
/// </summary>
public class HwiKatas
{
	#region SharedVariables

	// Bottleneck: user action on device.
	public TimeSpan ReasonableRequestTimeout { get; } = TimeSpan.FromMinutes(10);

	// Consolidating tx with 10 inputs and 1 output.
	public PSBT Psbt => PSBT.Parse("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", Network.TestNet);

	#endregion SharedVariables

	[Fact]
	public async Task TrezorTKataAsync()
	{
		// --- USER INTERACTIONS ---
		//
		// Connect and initialize your Trezor T with the following seed phrase:
		// more maid moon upgrade layer alter marine screen benefit way cover alcohol
		// Run this test.
		// displayaddress request: confirm 1 time
		// displayaddress request: confirm 1 time
		// signtx request: refuse 1 time
		// signtx request: Hold to confirm
		//
		// --- USER INTERACTIONS ---

		var network = Network.Main;
		var client = new HwiClient(network);
		using var cts = new CancellationTokenSource(ReasonableRequestTimeout);
		var enumerate = await client.EnumerateAsync(cts.Token);
		Assert.Single(enumerate);
		HwiEnumerateEntry entry = enumerate.Single();
		Assert.NotNull(entry.Path);
		Assert.Equal(HardwareWalletModels.Trezor_T, entry.Model);
		Assert.NotNull(entry.Fingerprint);

		string devicePath = entry.Path;
		HardwareWalletModels deviceType = entry.Model;
		HDFingerprint fingerprint = entry.Fingerprint!.Value;

		await Assert.ThrowsAsync<HwiException>(async () => await client.SetupAsync(deviceType, devicePath, false, cts.Token));

		await Assert.ThrowsAsync<HwiException>(async () => await client.RestoreAsync(deviceType, devicePath, false, cts.Token));

		// Trezor T doesn't support it.
		await Assert.ThrowsAsync<HwiException>(async () => await client.PromptPinAsync(deviceType, devicePath, cts.Token));
		// Trezor T doesn't support it.
		await Assert.ThrowsAsync<HwiException>(async () => await client.SendPinAsync(deviceType, devicePath, 1111, cts.Token));

		// Because of the Trezor T 2.3.5 firmware update,
		// we cannot use any longer the KeyManager.DefaultAccountKeyPath.
		KeyPath keyPath1 = new("m/84h/0h/0h/0/0");
		KeyPath keyPath2 = new("m/84h/0h/0h/0/1");
		ExtPubKey xpub1 = await client.GetXpubAsync(deviceType, devicePath, keyPath1, cts.Token);
		ExtPubKey xpub2 = await client.GetXpubAsync(deviceType, devicePath, keyPath2, cts.Token);
		Assert.NotNull(xpub1);
		Assert.NotNull(xpub2);
		Assert.NotEqual(xpub1, xpub2);

		// USER: CONFIRM
		BitcoinWitPubKeyAddress address1 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token);
		// USER: CONFIRM
		BitcoinWitPubKeyAddress address2 = await client.DisplayAddressAsync(fingerprint, keyPath2, cts.Token);
		Assert.NotNull(address1);
		Assert.NotNull(address2);
		Assert.NotEqual(address1, address2);
		var expectedAddress1 = xpub1.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
		var expectedAddress2 = xpub2.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
		Assert.Equal(expectedAddress1, address1);
		Assert.Equal(expectedAddress2, address2);

		// USER SHOULD REFUSE ACTION
		var result = await Assert.ThrowsAsync<HwiException>(async () => await client.SignTxAsync(deviceType, devicePath, Psbt, cts.Token));
		Assert.Equal(HwiErrorCode.ActionCanceled, result.ErrorCode);

		// USER: Hold to confirm
		PSBT signedPsbt = await client.SignTxAsync(deviceType, devicePath, Psbt, cts.Token);

		Transaction signedTx = signedPsbt.GetOriginalTransaction();
		Assert.Equal(Psbt.GetOriginalTransaction().GetHash(), signedTx.GetHash());

		var checkResult = signedTx.Check();
		Assert.Equal(TransactionCheckResult.Success, checkResult);
	}

	[Fact]
	public async Task TrezorOneKataAsync()
	{
		// --- USER INTERACTIONS ---
		//
		// Connect an already initialized device. Don't unlock it.
		// Run this test.
		//
		// --- USER INTERACTIONS ---

		var network = Network.Main;
		var client = new HwiClient(network);
		using var cts = new CancellationTokenSource(ReasonableRequestTimeout);
		var enumerate = await client.EnumerateAsync(cts.Token);
		Assert.Single(enumerate);
		HwiEnumerateEntry entry = enumerate.Single();
		Assert.NotNull(entry.Path);
		Assert.Equal(HardwareWalletModels.Trezor_1, entry.Model);
		Assert.True(entry.NeedsPinSent);
		Assert.Equal(HwiErrorCode.DeviceNotReady, entry.Code);
		Assert.Null(entry.Fingerprint);

		string devicePath = entry.Path;
		HardwareWalletModels deviceType = entry.Model;

		await Assert.ThrowsAsync<HwiException>(async () => await client.SetupAsync(deviceType, devicePath, false, cts.Token));

		await Assert.ThrowsAsync<HwiException>(async () => await client.RestoreAsync(deviceType, devicePath, false, cts.Token));
	}

	[Fact]
	public async Task ColdCardKataAsync()
	{
		// --- USER INTERACTIONS ---
		//
		// Connect and initialize your Coldcard with the following seed phrase:
		// more maid moon upgrade layer alter marine screen benefit way cover alcohol
		// Run this test.
		// signtx request: refuse
		// signtx request: confirm
		//
		// --- USER INTERACTIONS ---

		var network = Network.Main;
		var client = new HwiClient(network);
		using var cts = new CancellationTokenSource(ReasonableRequestTimeout);
		var enumerate = await client.EnumerateAsync(cts.Token);
		Assert.Single(enumerate);
		HwiEnumerateEntry entry = enumerate.Single();
		Assert.NotNull(entry.Path);
		Assert.Equal(HardwareWalletModels.Coldcard, entry.Model);
		Assert.NotNull(entry.Fingerprint);

		string devicePath = entry.Path;
		HardwareWalletModels deviceType = entry.Model;
		HDFingerprint fingerprint = entry.Fingerprint!.Value;

		// ColdCard doesn't support it.
		await Assert.ThrowsAsync<HwiException>(async () => await client.WipeAsync(deviceType, devicePath, cts.Token));

		// ColdCard doesn't support it.
		await Assert.ThrowsAsync<HwiException>(async () => await client.SetupAsync(deviceType, devicePath, false, cts.Token));

		// ColdCard doesn't support it.
		await Assert.ThrowsAsync<HwiException>(async () => await client.RestoreAsync(deviceType, devicePath, false, cts.Token));

		// ColdCard doesn't support it.
		await Assert.ThrowsAsync<HwiException>(async () => await client.PromptPinAsync(deviceType, devicePath, cts.Token));
		// ColdCard doesn't support it.
		await Assert.ThrowsAsync<HwiException>(async () => await client.SendPinAsync(deviceType, devicePath, 1111, cts.Token));

		KeyPath keyPath1 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit);
		KeyPath keyPath2 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive(1);
		ExtPubKey xpub1 = await client.GetXpubAsync(deviceType, devicePath, keyPath1, cts.Token);
		ExtPubKey xpub2 = await client.GetXpubAsync(deviceType, devicePath, keyPath2, cts.Token);
		Assert.NotNull(xpub1);
		Assert.NotNull(xpub2);
		Assert.NotEqual(xpub1, xpub2);

		// USER: REFUSE
		var ex = await Assert.ThrowsAsync<HwiException>(async () => await client.SignTxAsync(deviceType, devicePath, Psbt, cts.Token));
		Assert.Equal(HwiErrorCode.ActionCanceled, ex.ErrorCode);

		// USER: CONFIRM
		PSBT signedPsbt = await client.SignTxAsync(deviceType, devicePath, Psbt, cts.Token);

		Transaction signedTx = signedPsbt.GetOriginalTransaction();
		Assert.Equal(Psbt.GetOriginalTransaction().GetHash(), signedTx.GetHash());

		var checkResult = signedTx.Check();
		Assert.Equal(TransactionCheckResult.Success, checkResult);

		// ColdCard just display the address. There is no confirm/refuse action.

		BitcoinWitPubKeyAddress address1 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token);
		BitcoinWitPubKeyAddress address2 = await client.DisplayAddressAsync(fingerprint, keyPath2, cts.Token);
		Assert.NotNull(address1);
		Assert.NotNull(address2);
		Assert.NotEqual(address1, address2);
		var expectedAddress1 = xpub1.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
		var expectedAddress2 = xpub2.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
		Assert.Equal(expectedAddress1, address1);
		Assert.Equal(expectedAddress2, address2);
	}

	[Fact]
	public async Task LedgerNanoSKataAsync()
	{
		// --- USER INTERACTIONS ---
		//
		// Connect and initialize your Nano S with the following seed phrase:
		// more maid moon upgrade layer alter marine screen benefit way cover alcohol
		// Run this test.
		// displayaddress request(derivation path): approve
		// displayaddress request: reject
		// displayaddress request(derivation path): approve
		// displayaddress request: approve
		// displayaddress request(derivation path): approve
		// displayaddress request: approve
		// signtx request: reject
		// signtx request: accept
		// confirm transaction: accept and send
		//
		// --- USER INTERACTIONS ---

		var network = Network.Main;
		var client = new HwiClient(network);
		using var cts = new CancellationTokenSource(ReasonableRequestTimeout);
		var enumerate = await client.EnumerateAsync(cts.Token);
		HwiEnumerateEntry entry = Assert.Single(enumerate);
		Assert.NotNull(entry.Path);
		Assert.Equal(HardwareWalletModels.Ledger_Nano_S, entry.Model);
		Assert.NotNull(entry.Fingerprint);
		Assert.Null(entry.Code);
		Assert.Null(entry.Error);
		Assert.Null(entry.SerialNumber);
		Assert.False(entry.NeedsPassphraseSent);
		Assert.False(entry.NeedsPinSent);

		string devicePath = entry.Path;
		HardwareWalletModels deviceType = entry.Model;
		HDFingerprint fingerprint = entry.Fingerprint!.Value;

		await Assert.ThrowsAsync<HwiException>(async () => await client.SetupAsync(deviceType, devicePath, false, cts.Token));

		await Assert.ThrowsAsync<HwiException>(async () => await client.RestoreAsync(deviceType, devicePath, false, cts.Token));

		await Assert.ThrowsAsync<HwiException>(async () => await client.PromptPinAsync(deviceType, devicePath, cts.Token));

		await Assert.ThrowsAsync<HwiException>(async () => await client.SendPinAsync(deviceType, devicePath, 1111, cts.Token));

		KeyPath keyPath1 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive("0/0");
		KeyPath keyPath2 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive("0/1");
		ExtPubKey xpub1 = await client.GetXpubAsync(deviceType, devicePath, keyPath1, cts.Token);
		ExtPubKey xpub2 = await client.GetXpubAsync(deviceType, devicePath, keyPath2, cts.Token);
		Assert.NotNull(xpub1);
		Assert.NotNull(xpub2);
		Assert.NotEqual(xpub1, xpub2);

		// USER SHOULD REFUSE ACTION
		await Assert.ThrowsAsync<HwiException>(async () => await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token));

		// USER: CONFIRM
		BitcoinWitPubKeyAddress address1 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token);
		// USER: CONFIRM
		BitcoinWitPubKeyAddress address2 = await client.DisplayAddressAsync(fingerprint, keyPath2, cts.Token);
		Assert.NotNull(address1);
		Assert.NotNull(address2);
		Assert.NotEqual(address1, address2);
		var expectedAddress1 = xpub1.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
		var expectedAddress2 = xpub2.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
		Assert.Equal(expectedAddress1, address1);
		Assert.Equal(expectedAddress2, address2);

		// USER: REFUSE
		var ex = await Assert.ThrowsAsync<HwiException>(async () => await client.SignTxAsync(deviceType, devicePath, Psbt, cts.Token));
		Assert.Equal(HwiErrorCode.UnknownError, ex.ErrorCode);

		// USER: CONFIRM
		PSBT signedPsbt = await client.SignTxAsync(deviceType, devicePath, Psbt, cts.Token);

		Transaction signedTx = signedPsbt.GetOriginalTransaction();
		Assert.Equal(Psbt.GetOriginalTransaction().GetHash(), signedTx.GetHash());

		var checkResult = signedTx.Check();
		Assert.Equal(TransactionCheckResult.Success, checkResult);
	}

	[Fact]
	public async Task LedgerNanoSPlusKataAsync()
	{
		// --- USER INTERACTIONS ---
		//
		// Connect and initialize your Nano S+ with the following seed phrase:
		// more maid moon upgrade layer alter marine screen benefit way cover alcohol
		// Run this test.
		// displayaddress request(derivation path): approve
		// displayaddress request: reject
		// displayaddress request(derivation path): approve
		// displayaddress request: approve
		// displayaddress request(derivation path): approve
		// displayaddress request: approve
		// signtx request: reject
		// signtx request: accept
		// confirm transaction: accept and send
		//
		// --- USER INTERACTIONS ---

		var network = Network.Main;
		var client = new HwiClient(network);
		using var cts = new CancellationTokenSource(ReasonableRequestTimeout);
		var enumerate = await client.EnumerateAsync(cts.Token);
		HwiEnumerateEntry entry = Assert.Single(enumerate);
		Assert.NotNull(entry.Path);
		Assert.Equal(HardwareWalletModels.Ledger_Nano_S_Plus, entry.Model);
		Assert.NotNull(entry.Fingerprint);
		Assert.Null(entry.Code);
		Assert.Null(entry.Error);
		Assert.Null(entry.SerialNumber);
		Assert.False(entry.NeedsPassphraseSent);
		Assert.False(entry.NeedsPinSent);

		string devicePath = entry.Path;
		HardwareWalletModels deviceType = entry.Model;
		HDFingerprint fingerprint = entry.Fingerprint!.Value;

		await Assert.ThrowsAsync<HwiException>(async () => await client.SetupAsync(deviceType, devicePath, false, cts.Token));

		await Assert.ThrowsAsync<HwiException>(async () => await client.RestoreAsync(deviceType, devicePath, false, cts.Token));

		await Assert.ThrowsAsync<HwiException>(async () => await client.PromptPinAsync(deviceType, devicePath, cts.Token));

		await Assert.ThrowsAsync<HwiException>(async () => await client.SendPinAsync(deviceType, devicePath, 1111, cts.Token));

		KeyPath keyPath1 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive("0/0");
		KeyPath keyPath2 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive("0/1");
		ExtPubKey xpub1 = await client.GetXpubAsync(deviceType, devicePath, keyPath1, cts.Token);
		ExtPubKey xpub2 = await client.GetXpubAsync(deviceType, devicePath, keyPath2, cts.Token);
		Assert.NotNull(xpub1);
		Assert.NotNull(xpub2);
		Assert.NotEqual(xpub1, xpub2);

		// USER SHOULD REFUSE ACTION
		await Assert.ThrowsAsync<HwiException>(async () => await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token));

		// USER: CONFIRM
		BitcoinWitPubKeyAddress address1 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token);
		// USER: CONFIRM
		BitcoinWitPubKeyAddress address2 = await client.DisplayAddressAsync(fingerprint, keyPath2, cts.Token);
		Assert.NotNull(address1);
		Assert.NotNull(address2);
		Assert.NotEqual(address1, address2);
		var expectedAddress1 = xpub1.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
		var expectedAddress2 = xpub2.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
		Assert.Equal(expectedAddress1, address1);
		Assert.Equal(expectedAddress2, address2);

		// USER: REFUSE
		var ex = await Assert.ThrowsAsync<HwiException>(async () => await client.SignTxAsync(deviceType, devicePath, Psbt, cts.Token));
		Assert.Equal(HwiErrorCode.UnknownError, ex.ErrorCode);

		// USER: CONFIRM
		PSBT signedPsbt = await client.SignTxAsync(deviceType, devicePath, Psbt, cts.Token);

		Transaction signedTx = signedPsbt.GetOriginalTransaction();
		Assert.Equal(Psbt.GetOriginalTransaction().GetHash(), signedTx.GetHash());

		var checkResult = signedTx.Check();
		Assert.Equal(TransactionCheckResult.Success, checkResult);
	}

	[Fact]
	public async Task LedgerNanoXKataAsync()
	{
		// --- USER INTERACTIONS ---
		//
		// Connect and initialize your Nano X with the following seed phrase:
		// more maid moon upgrade layer alter marine screen benefit way cover alcohol
		// Run this test.
		// displayaddress request(derivation path): approve
		// displayaddress request: reject
		// displayaddress request(derivation path): approve
		// displayaddress request: approve
		// displayaddress request(derivation path): approve
		// displayaddress request: approve
		// signtx request: reject
		// signtx request: accept
		// confirm transaction: accept and send
		//
		// --- USER INTERACTIONS ---

		var network = Network.Main;
		var client = new HwiClient(network);
		using var cts = new CancellationTokenSource(ReasonableRequestTimeout);
		var enumerate = await client.EnumerateAsync(cts.Token);
		HwiEnumerateEntry entry = Assert.Single(enumerate);
		Assert.NotNull(entry.Path);
		Assert.Equal(HardwareWalletModels.Ledger_Nano_X, entry.Model);
		Assert.NotNull(entry.Fingerprint);
		Assert.Null(entry.Code);
		Assert.Null(entry.Error);
		Assert.Null(entry.SerialNumber);
		Assert.False(entry.NeedsPassphraseSent);
		Assert.False(entry.NeedsPinSent);

		string devicePath = entry.Path;
		HardwareWalletModels deviceType = entry.Model;
		HDFingerprint fingerprint = entry.Fingerprint!.Value;

		await Assert.ThrowsAsync<HwiException>(async () => await client.SetupAsync(deviceType, devicePath, false, cts.Token));

		await Assert.ThrowsAsync<HwiException>(async () => await client.RestoreAsync(deviceType, devicePath, false, cts.Token));

		await Assert.ThrowsAsync<HwiException>(async () => await client.PromptPinAsync(deviceType, devicePath, cts.Token));

		await Assert.ThrowsAsync<HwiException>(async () => await client.SendPinAsync(deviceType, devicePath, 1111, cts.Token));

		KeyPath keyPath1 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive("0/0");
		KeyPath keyPath2 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive("0/1");

		ExtPubKey xpub1 = await client.GetXpubAsync(deviceType, devicePath, keyPath1, cts.Token);
		ExtPubKey xpub2 = await client.GetXpubAsync(deviceType, devicePath, keyPath2, cts.Token);
		Assert.NotNull(xpub1);
		Assert.NotNull(xpub2);
		Assert.NotEqual(xpub1, xpub2);

		// USER SHOULD REFUSE ACTION
		await Assert.ThrowsAsync<HwiException>(async () => await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token));

		// USER: CONFIRM
		BitcoinWitPubKeyAddress address1 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token);
		// USER: CONFIRM
		BitcoinWitPubKeyAddress address2 = await client.DisplayAddressAsync(fingerprint, keyPath2, cts.Token);
		Assert.NotNull(address1);
		Assert.NotNull(address2);
		Assert.NotEqual(address1, address2);
		var expectedAddress1 = xpub1.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
		var expectedAddress2 = xpub2.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
		Assert.Equal(expectedAddress1, address1);
		Assert.Equal(expectedAddress2, address2);

		// USER: REFUSE
		var ex = await Assert.ThrowsAsync<HwiException>(async () => await client.SignTxAsync(deviceType, devicePath, Psbt, cts.Token));
		Assert.Equal(HwiErrorCode.UnknownError, ex.ErrorCode);

		// USER: CONFIRM
		PSBT signedPsbt = await client.SignTxAsync(deviceType, devicePath, Psbt, cts.Token);

		Transaction signedTx = signedPsbt.GetOriginalTransaction();
		Assert.Equal(Psbt.GetOriginalTransaction().GetHash(), signedTx.GetHash());

		var checkResult = signedTx.Check();
		Assert.Equal(TransactionCheckResult.Success, checkResult);
	}

	[Fact]
	public async Task JadeKataAsync()
	{
		// --- USER INTERACTIONS ---
		//
		// Connect and initialize your Jade with the following seed phrase:
		// more maid moon upgrade layer alter marine screen benefit way cover alcohol
		// Run this test.
		// displayaddress request by device_type: reject
		// displayaddress request by device_type: approve
		// displayaddress request by fingerprint: approve
		// signtx request: reject
		// signtx request: 2x approve
		//
		// --- USER INTERACTIONS ---

		var network = Network.Main;
		var client = new HwiClient(network);
		using var cts = new CancellationTokenSource(ReasonableRequestTimeout);
		var enumerate = await client.EnumerateAsync(cts.Token);
		HwiEnumerateEntry entry = Assert.Single(enumerate);
		Assert.NotNull(entry.Path);
		Assert.Equal(HardwareWalletModels.Jade, entry.Model);
		Assert.True(HwiValidationHelper.ValidatePathString(entry.Model, entry.Path));
		Assert.NotNull(entry.Fingerprint);
		Assert.Null(entry.Code);
		Assert.Null(entry.Error);
		Assert.True(string.IsNullOrEmpty(entry.SerialNumber));
		Assert.False(entry.NeedsPassphraseSent);
		Assert.False(entry.NeedsPinSent);

		string devicePath = entry.Path;
		HardwareWalletModels deviceType = entry.Model;
		HDFingerprint fingerprint = entry.Fingerprint!.Value;

		await Assert.ThrowsAsync<HwiException>(async () => await client.SetupAsync(deviceType, devicePath, false, cts.Token));

		await Assert.ThrowsAsync<HwiException>(async () => await client.RestoreAsync(deviceType, devicePath, false, cts.Token));

		await Assert.ThrowsAsync<HwiException>(async () => await client.PromptPinAsync(deviceType, devicePath, cts.Token));

		await Assert.ThrowsAsync<HwiException>(async () => await client.SendPinAsync(deviceType, devicePath, 1111, cts.Token));

		KeyPath keyPath1 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive("0/0");
		KeyPath keyPath2 = KeyManager.GetAccountKeyPath(network, ScriptPubKeyType.Segwit).Derive("0/1");

		ExtPubKey xpub1 = await client.GetXpubAsync(deviceType, devicePath, keyPath1, cts.Token);
		ExtPubKey xpub2 = await client.GetXpubAsync(deviceType, devicePath, keyPath2, cts.Token);
		Assert.NotNull(xpub1);
		Assert.NotNull(xpub2);
		Assert.NotEqual(xpub1, xpub2);

		// USER SHOULD REFUSE ACTION
		await Assert.ThrowsAsync<HwiException>(async () => await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token));

		// USER: CONFIRM
		BitcoinWitPubKeyAddress address1 = await client.DisplayAddressAsync(deviceType, devicePath, keyPath1, cts.Token);
		// USER: CONFIRM
		BitcoinWitPubKeyAddress address2 = await client.DisplayAddressAsync(fingerprint, keyPath2, cts.Token);
		Assert.NotNull(address1);
		Assert.NotNull(address2);
		Assert.NotEqual(address1, address2);
		var expectedAddress1 = xpub1.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
		var expectedAddress2 = xpub2.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
		Assert.Equal(expectedAddress1, address1);
		Assert.Equal(expectedAddress2, address2);

		// USER: REFUSE
		var ex = await Assert.ThrowsAsync<HwiException>(async () => await client.SignTxAsync(deviceType, devicePath, Psbt, cts.Token));
		Assert.Equal(HwiErrorCode.ActionCanceled, ex.ErrorCode);

		// USER: CONFIRM CONFIRM
		PSBT signedPsbt = await client.SignTxAsync(deviceType, devicePath, Psbt, cts.Token);

		Transaction signedTx = signedPsbt.GetOriginalTransaction();
		Assert.Equal(Psbt.GetOriginalTransaction().GetHash(), signedTx.GetHash());

		var checkResult = signedTx.Check();
		Assert.Equal(TransactionCheckResult.Success, checkResult);
	}

	[Fact]
	public async Task ColdCardPsbtKataAsync()
	{
		// --- USER INTERACTIONS ---
		//
		// Connect and initialize your ColdCard with the following seed phrase:
		// more maid moon upgrade layer alter marine screen benefit way cover alcohol
		// Before you start the test:
		// 1. Make sure SD card is not connected to the computer.
		// Run this test.
		//
		// 1. Beep sound.
		// 2. Plug the SD card into the computer.
		// 3. Beep sound.
		// 2. Unplug the SD card from the computer.
		// 3. Plug the SD card into the ColdCard.
		// 4. Try to sign the transaction (Ready to sign).
		// 5. If the transaction is signed without error, then plug the SD card into the computer else wait for the beep sound.
		// 6. Beep sound.
		//
		// --- USER INTERACTIONS ---
		var network = Network.Main;
		Console.Beep();

		var newDrive = await UsbAttachHelper.DetectNewDriveAsync();
		// Insert the SD card into the computer.
		Assert.NotNull(newDrive);

		// Consolidating tx with 10 inputs and 1 output.
		PSBT coldCardPsbt = PSBT.Parse(
			"cHNidP8BAHEBAAAAAbUwDqJlIViUTsXtSM/1IErUC9Xh4SF/6NKBm7zVb1W9AAAAAAD9////AhM8AAAAAAAAFgAUBRkjCvL5zaksawVCiozhXY+jZ8vACAAAAAAAABYAFCZua7ILm6F4bNWykw9wgeVD5s0VAAAAAAABAR9zVgAAAAAAABYAFNAycvKl+jDeu23kRyGQ2ZJ1MgYkAQB9AQAAAAFKFZo0tmZ8bL5tIw6RDPrVB++6ZEzEkWeO3p7eQtpBZAAAAAAA/f///wJzVgAAAAAAABYAFNAycvKl+jDeu23kRyGQ2ZJ1MgYka71hAAAAAAAiUSCdmcl1eIg8Xewvv+HYh34kHENr175Nx0hvleXmc5cIMAAAAAAiBgMDjnKMrVTFG6rS2lkJ5Abs2eQLEpMwb12jIRU83l/5LRjl28nLVAAAgAAAAIAAAACAAAAAAAsAAAAAACICAsd5ZUg1yxznPEp3vwCtmzBBEC3lHkIpIltXojE9SDZLGOXbyctUAACAAAAAgAAAAIABAAAABgAAAAA=", network);

		string initialFileName = coldCardPsbt.GetOriginalTransaction().GetHash().ToString();
		string fileName = Path.Combine(newDrive, initialFileName + ".psbt");

		if (File.Exists(fileName))
		{
			File.Delete(fileName);
		}

		await File.WriteAllBytesAsync(fileName, coldCardPsbt.ToBytes());

		Console.Beep();

		newDrive = await UsbAttachHelper.DetectNewDriveAsync();

		Assert.NotNull(newDrive);

		fileName = Path.Combine(newDrive, initialFileName + ".psbt");
		Assert.True(File.Exists(fileName));

		var importedTransaction = Mock.Of<ITransactionBroadcasterModel>().LoadFromFileAsync(fileName);

		Assert.NotNull(importedTransaction);
		Console.Beep();
		// Unplug the SD card from the computer.
	}

	[Fact]
	public async Task ColdCardImportWalletKataAsync()
	{
		// --- USER INTERACTIONS ---
		//
		// Connect and initialize your ColdCard with the following seed phrase:
		// more maid moon upgrade layer alter marine screen benefit way cover alcohol
		// Before you start the test:
		// 1. Make sure that the SD card is inserted into the ColdCard.
		// 2. Make sure that the exported wallet wasabi file is on the sd card (Advanced > MicroSD Card > Export Wallet > WalletWasabi).
		// 3. Make sure SD card is not connected to the computer.
		// 4. Make sure that WasabiWallet file name is "new-wasabi.json".
		// Run this test.
		//
		// 1. Beep sound.
		// 2. Plug the SD card into the computer,from the ColdCard.
		// 3. Beep sound.
		// 2. Unplug the SD card from the computer.
		// 3. Plug the SD card into the ColdCard.
		// 4. Try to sign the transaction (Ready to sign).
		// 5. If the transaction is signed without error, then plug the SD card into the computer else wait for the beep sound.
		// 6. Beep sound.
		//
		// --- USER INTERACTIONS ---

		#region SetupRegTestNetwork

		await using RegTestSetup setup = await RegTestSetup.InitializeTestEnvironmentAsync(new RegTestFixture(), numberOfBlocksToGenerate: 1);
		IRPCClient rpc = setup.RpcClient;
		Network network = setup.Network;
		BitcoinStore bitcoinStore = setup.BitcoinStore;
		using Backend.Global global = setup.Global;
		ServiceConfiguration serviceConfiguration = setup.ServiceConfiguration;
		string password = setup.Password;

		bitcoinStore.IndexStore.NewFilters += setup.Wallet_NewFiltersProcessed;

		// Create the services.
		// 1. Create connection service.
		NodesGroup nodes = new(global.Config.Network, requirements: Constants.NodeRequirements);
		nodes.ConnectedNodes.Add(await new RegTestFixture().BackendRegTestNode.CreateNewP2pNodeAsync());

		// 2. Create mempool service.

		Node node = await new RegTestFixture().BackendRegTestNode.CreateNewP2pNodeAsync();
		node.Behaviors.Add(bitcoinStore.CreateUntrustedP2pBehavior());

		// 3. Create wasabi synchronizer service.
		await using WasabiHttpClientFactory httpClientFactory = new(torEndPoint: null, backendUriGetter: () => new Uri(new RegTestFixture().BackendEndPoint));
		WasabiSynchronizer synchronizer = new(requestInterval: TimeSpan.FromSeconds(3), 10000, bitcoinStore, httpClientFactory);
		HybridFeeProvider feeProvider = new(synchronizer, null);

		// 4. Create key manager service.
		var keyManager = KeyManager.CreateNew(out _, password, network);

		// 5. Create wallet service.
		var workDir = Common.GetWorkDir();

		using MemoryCache cache = BitcoinFactory.CreateMemoryCache();
		await using SpecificNodeBlockProvider specificNodeBlockProvider = new(network, serviceConfiguration, httpClientFactory.TorEndpoint);

		var blockProvider = new SmartBlockProvider(
			bitcoinStore.BlockRepository,
			rpcBlockProvider: null,
			specificNodeBlockProvider,
			new P2PBlockProvider(network, nodes, httpClientFactory.IsTorEnabled),
			cache);

		WalletManager walletManager = new(network, workDir, new WalletDirectories(network, workDir), bitcoinStore, synchronizer, feeProvider, blockProvider, serviceConfiguration);
		walletManager.Initialize();

		#endregion SetupRegTestNetwork

		Console.Beep();

		var newDrive = await UsbAttachHelper.DetectNewDriveAsync();
		// Insert the SD card into the computer.
		Assert.NotNull(newDrive);

		var walletPath = Path.Combine(newDrive, "new-wasabi.json");
		Assert.True(File.Exists(walletPath));

		var readColdCardWasabiWalletText = await File.ReadAllTextAsync(walletPath);
		string pattern = "\\{\"ExtPubKey\":\\s*\"(xpub[1-9A-HJ-NP-Za-km-z]+)\",\\s*\"MasterFingerprint\":\\s*\"([0-9A-Fa-f]+)\",\\s*\"ColdCardFirmwareVersion\":\\s*\"(\\d+\\.\\d+\\.\\d+)\"\\}";

		//string pattern = @"\""ExtPubKey\"":\s*\""xpub[^\""]+"",\s *\""MasterFingerprint\"":\s *\""([0 - 9A - F]{ 8})"",\s *\""ColdCardFirmwareVersion\"":\s *\""(\d +\.\d +\.\d +)""";

		Assert.Matches(pattern, readColdCardWasabiWalletText);

		var wallet = await ImportWalletHelper.ImportWalletAsync(walletManager, "coldCardTest", walletPath);
	}
}

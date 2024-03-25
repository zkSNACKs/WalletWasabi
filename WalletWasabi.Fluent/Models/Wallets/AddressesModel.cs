using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionProcessing;
using WalletWasabi.Fluent.Infrastructure;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.Models.Wallets;

[AppLifetime]
[AutoInterface]
public partial class AddressesModel
{
	private readonly ISubject<HdPubKey> _newAddressGenerated = new Subject<HdPubKey>();
	private readonly Wallet _wallet;
	private readonly SourceList<HdPubKey> _source;

	public AddressesModel(Wallet wallet)
	{
		_wallet = wallet;
		_source = new SourceList<HdPubKey>();
		_source.AddRange(GetUnusedKeys());

		Observable.FromEventPattern<ProcessedResult>(
				h => wallet.WalletRelevantTransactionProcessed += h,
				h => wallet.WalletRelevantTransactionProcessed -= h)
			.Do(_ => UpdateUnusedKeys())
			.Subscribe();

		_newAddressGenerated
			.Do(address => _source.Add(address))
			.Subscribe();

		_source.Connect()
			.Transform(key => (IAddress) new Address(_wallet.KeyManager, key, Hide))
			.Bind(out var unusedAddresses)
			.Subscribe();

		Unused = unusedAddresses;
	}

	private IEnumerable<HdPubKey> GetUnusedKeys() => _wallet.KeyManager.GetKeys(x => x is { IsInternal: false, KeyState: KeyState.Clean, Labels.Count: > 0 });

	public IAddress NextReceiveAddress(IEnumerable<string> destinationLabels)
	{
		var pubKey = _wallet.GetNextReceiveAddress(destinationLabels);
		var nextReceiveAddress = new Address(_wallet.KeyManager, pubKey, Hide);
		_newAddressGenerated.OnNext(pubKey);

		return nextReceiveAddress;
	}

	public ReadOnlyObservableCollection<IAddress> Unused { get; }

	public void Hide(Address address)
	{
		_wallet.KeyManager.SetKeyState(KeyState.Locked, address.HdPubKey);
		_wallet.KeyManager.ToFile();
		_source.Remove(address.HdPubKey);
	}

	private void UpdateUnusedKeys()
	{
		var itemsToRemove = _source.Items
			.Where(item => item.KeyState != KeyState.Clean)
			.ToList();

		foreach (var item in itemsToRemove)
		{
			_source.Remove(item);
		}
	}
}

using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NBitcoin;

namespace WalletWasabi.Blockchain.Keys;

public record HdPubKeyCacheEntry(HdPubKey HdPubKey, byte[] ScriptPubKeyBytes, ScriptPubKeyType ScriptPubKeyType);

public class HdPubKeyCache : IEnumerable<HdPubKeyCacheEntry>
{
	private Dictionary<Script, HdPubKeyCacheEntry> CacheEntries { get; } = new ();

	public void AddKey(HdPubKey hdPubKey, ScriptPubKeyType scriptPubKeyType)
	{
		var scriptPubKey = hdPubKey.PubKey.GetScriptPubKey(scriptPubKeyType);
		CacheEntries.AddOrReplace(scriptPubKey, new HdPubKeyCacheEntry(hdPubKey, scriptPubKey.ToCompressedBytes(), scriptPubKeyType));
	}

	public bool TryGetPubKey(Script destination, [NotNullWhen(true)] out HdPubKeyCacheEntry? hdPubKeyEx) =>
		CacheEntries.TryGetValue(destination, out hdPubKeyEx);

	public IEnumerable<HdPubKey> AddRangeKeys(IEnumerable<HdPubKey> keys)
	{
		foreach (var key in keys)
		{
			AddKey(key, ScriptPubKeyType.Segwit);
		}

		return keys;
	}

	public IEnumerator<HdPubKeyCacheEntry> GetEnumerator() =>
		CacheEntries.Values
			.DistinctBy(x => x.HdPubKey)
			.OrderBy(x => x.HdPubKey.Index)
			.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() =>
		GetEnumerator();

	private HdPubKeyGlobalView Snapshot =>
		new(this.Select(x => x.HdPubKey).ToImmutableList());
	
	public HdPubKeyPathView GetView(KeyPath keyPath) =>
		Snapshot.GetChildKeyOf(keyPath);
}

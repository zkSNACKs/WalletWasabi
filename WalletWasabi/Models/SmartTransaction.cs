using NBitcoin;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using WalletWasabi.Helpers;
using WalletWasabi.JsonConverters;

namespace WalletWasabi.Models
{
	[JsonObject(MemberSerialization.OptIn)]
	public class SmartTransaction : IEquatable<SmartTransaction>, IEquatable<Transaction>
	{
		#region Members

		[JsonProperty]
		[JsonConverter(typeof(TransactionJsonConverter))]
		public Transaction Transaction { get; }

		[JsonProperty]
		[JsonConverter(typeof(HeightJsonConverter))]
		public Height Height { get; private set; }

		[JsonProperty]
		[JsonConverter(typeof(Uint256JsonConverter))]
		public uint256 BlockHash { get; private set; }

		[JsonProperty]
		public int BlockIndex { get; private set; }

		[JsonProperty]
		public string Label { get; set; }

		/// <summary>
		/// if Height is Mempool it's first seen, else null,
		/// only exists in memory,
		/// does not affect equality
		/// </summary>
		[JsonProperty]
		[JsonConverter(typeof(BlockCypherDateTimeOffsetJsonConverter))]
		public DateTimeOffset? FirstSeenIfMempoolTime { get; private set; }

		[JsonProperty]
		public bool IsReplacement { get; private set; }

		/// <summary>
		/// A transaction can signal that is replaceable by fee in two ways:
		/// * Explicitly by using a nSequence &lt; (0xffffffff - 1) or,
		/// * Implicitly in case one of its unconfirmed ancestors are replaceable
		/// </summary>
		public bool IsRBF => !Confirmed && (Transaction.RBF || IsReplacement);

		public bool Confirmed => Height.Type == HeightType.Chain;

		public uint256 GetHash() => Transaction.GetHash();

		public int GetConfirmationCount(Height bestHeight) => Height == Height.Mempool ? 0 : bestHeight.Value - Height.Value + 1;

		#endregion Members

		#region Constructors

		[JsonConstructor]
		public SmartTransaction(Transaction transaction, Height height, uint256 blockHash = null, int blockIndex = 0, string label = "", DateTimeOffset? firstSeenIfMempoolTime = null, bool isReplacement = false)
		{
			Transaction = transaction;
			Label = CorrectLabel(label);

			SetHeight(height, blockHash, blockIndex);
			if (firstSeenIfMempoolTime != null)
			{
				FirstSeenIfMempoolTime = firstSeenIfMempoolTime;
			}
			IsReplacement = isReplacement;
		}

		#endregion Constructors

		public string ToLine()
		{
			// GetHash is also serialized, so file can be interpreted with our eyes better.

			return string.Join(':',
				GetHash(),
				Transaction.ToHex(),
				Height,
				BlockHash,
				BlockIndex,
				CorrectLabel(Label),
				FirstSeenIfMempoolTime?.ToUnixTimeSeconds(),
				IsReplacement);
		}

		private string CorrectLabel(string label)
		{
			return Guard.Correct(label).Replace(":", "", StringComparison.Ordinal).Trim();
		}

		public static SmartTransaction FromLine(string line, Network expectedNetwork)
		{
			line = Guard.NotNullOrEmptyOrWhitespace(nameof(line), line, trim: true);
			expectedNetwork = Guard.NotNull(nameof(expectedNetwork), expectedNetwork);

			var parts = line.Split(':', StringSplitOptions.None).Select(x => x.Trim()).ToArray();

			// Find the Transaction hex, it must be always present.
			ParseTransactionHex(expectedNetwork, parts, out Transaction tx, out int txHexIndex);

			try
			{
				// First is redundand txhash serialization.
				var heightString = parts[txHexIndex + 1];
				var blockHashString = parts[txHexIndex + 2];
				var blockIndexString = parts[txHexIndex + 3];
				var label = parts[txHexIndex + 4];
				var firstSeenIfMempoolTimeString = parts[txHexIndex + 5];
				var isReplacementString = parts[txHexIndex + 6];

				if (!Height.TryParse(heightString, out Height h))
				{
					h = Height.Unknown;
				}
				if (!uint256.TryParse(blockHashString, out uint256 bh))
				{
					bh = null;
				}
				if (!int.TryParse(blockIndexString, out int bi))
				{
					bi = 0;
				}
				DateTimeOffset? fs = null;
				if (long.TryParse(firstSeenIfMempoolTimeString, out long unixSeconds))
				{
					fs = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
				}
				if (!bool.TryParse(isReplacementString, out bool ir))
				{
					ir = false;
				}

				return new SmartTransaction(tx, h, bh, bi, label, fs, ir);
			}
			catch
			{
				return new SmartTransaction(tx, Height.Unknown);
			}
		}

		private static void ParseTransactionHex(Network expectedNetwork, string[] parts, out Transaction tx, out int txHexIndex)
		{
			tx = null;
			txHexIndex = 1;
			try
			{
				tx = Transaction.Parse(parts[txHexIndex], expectedNetwork);
			}
			catch
			{
				for (txHexIndex = 0; txHexIndex < parts.Length; txHexIndex++)
				{
					if (txHexIndex == 1)
					{
						continue; // We already checked this.
					}

					string part = parts[txHexIndex];
					try
					{
						tx = Transaction.Parse(part, expectedNetwork);
						break;
					}
					catch
					{
						continue;
					}
				}
			}
			if (tx is null)
			{
				throw new FormatException($"Transaction hex is not present.");
			}
		}

		public void SetHeight(Height height, uint256 blockHash = null, int blockIndex = 0)
		{
			Height = height;
			if (height == Height.Mempool)
			{
				FirstSeenIfMempoolTime = DateTimeOffset.UtcNow;
			}
			else
			{
				FirstSeenIfMempoolTime = null;
			}

			BlockHash = blockHash;
			BlockIndex = blockIndex;
		}

		public void SetReplacement()
		{
			IsReplacement = true;
		}

		public bool HasLabel() => !string.IsNullOrWhiteSpace(Label);

		/// <summary>
		/// First looks at height, then block index, then mempool firstseen.
		/// </summary>
		public static IComparer<SmartTransaction> GetBlockchainComparer()
		{
			return Comparer<SmartTransaction>.Create((a, b) =>
			{
				var heightCompareResult = a.Height.CompareTo(b.Height);
				if (heightCompareResult != 0)
				{
					return heightCompareResult;
				}

				// If mempool this should be 0, so they should be equal so no worry about it.
				var blockIndexCompareResult = a.BlockIndex.CompareTo(b.BlockIndex);
				if (blockIndexCompareResult != 0)
				{
					return blockIndexCompareResult;
				}

				var firstSeenCompareResult = (a.FirstSeenIfMempoolTime ?? DateTime.UtcNow).CompareTo(b.FirstSeenIfMempoolTime ?? DateTime.UtcNow);
				return firstSeenCompareResult;
			});
		}

		#region Equality

		public bool Equals(SmartTransaction other) => GetHash().Equals(other?.GetHash());

		public bool Equals(Transaction other) => GetHash().Equals(other?.GetHash());

		public override bool Equals(object obj) =>
			obj is SmartTransaction transaction && this == transaction;

		public override int GetHashCode() => GetHash().GetHashCode();

		public static bool operator !=(SmartTransaction tx1, SmartTransaction tx2) => !(tx1 == tx2);

		public static bool operator ==(SmartTransaction tx1, SmartTransaction tx2)
		{
			bool rc;

			if (ReferenceEquals(tx1, tx2))
			{
				rc = true;
			}
			else if (tx1 is null || tx2 is null)
			{
				rc = false;
			}
			else
			{
				rc = tx1.GetHash().Equals(tx2.GetHash());
			}

			return rc;
		}

		public static bool operator ==(Transaction tx1, SmartTransaction tx2)
		{
			bool rc = tx1 is null || tx2 is null ? false : tx1.GetHash().Equals(tx2.GetHash());
			return rc;
		}

		public static bool operator !=(Transaction tx1, SmartTransaction tx2) => !(tx1 == tx2);

		public static bool operator ==(SmartTransaction tx1, Transaction tx2)
		{
			bool rc = tx1 is null || tx2 is null ? false : tx1.GetHash().Equals(tx2.GetHash());
			return rc;
		}

		public static bool operator !=(SmartTransaction tx1, Transaction tx2) => !(tx1 == tx2);

		#endregion Equality
	}
}

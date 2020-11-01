using System;
using WalletWasabi.Helpers;
using WalletWasabi.Tor.Socks5.Models.Interfaces;

namespace WalletWasabi.Tor.Socks5.Models.Bases
{
	public abstract class OctetSerializableBase : IByteSerializable, IEquatable<OctetSerializableBase>, IEquatable<byte>
	{
		protected byte ByteValue { get; set; }

		#region Serialization

		public byte ToByte() => ByteValue;

		public void FromByte(byte b) => ByteValue = b;

		public string ToHex(bool xhhSyntax = false)
		{
			if (xhhSyntax)
			{
				return $"X'{ByteHelpers.ToHex(ToByte())}'";
			}
			return ByteHelpers.ToHex(ToByte());
		}

		public void FromHex(string hex)
		{
			hex = Guard.NotNullOrEmptyOrWhitespace(nameof(hex), hex, true);

			byte[] bytes = ByteHelpers.FromHex(hex);
			if (bytes.Length != 1)
			{
				throw new FormatException($"{nameof(hex)} must be exactly one byte. Actual: {bytes.Length} bytes. Value: {hex}.");
			}

			ByteValue = bytes[0];
		}

		public override string ToString()
		{
			return ToHex(xhhSyntax: true);
		}

		#endregion Serialization

		#region EqualityAndComparison

		public static bool operator ==(OctetSerializableBase x, OctetSerializableBase y) => x.Equals(y);

		public static bool operator !=(OctetSerializableBase x, OctetSerializableBase y) => !(x == y);

		public static bool operator ==(byte x, OctetSerializableBase y) => x == y?.ByteValue;

		public static bool operator ==(OctetSerializableBase x, byte y) => x?.ByteValue == y;

		public static bool operator !=(byte x, OctetSerializableBase y) => !(x == y);

		public static bool operator !=(OctetSerializableBase x, byte y) => !(x == y);

		/// <inheritdoc/>
		public override bool Equals(object? obj)
		{
			if (obj == null)
			{
				return false;
			}
			else if (obj is OctetSerializableBase other)
			{
				return Equals(other);
			}

			return false;
		}

		/// <inheritdoc/>
		public bool Equals(OctetSerializableBase? other) => other is { } && ToByte() == other.ToByte();

		public override int GetHashCode() => ByteValue;

		public bool Equals(byte other) => ByteValue == other;

		#endregion EqualityAndComparison
	}
}

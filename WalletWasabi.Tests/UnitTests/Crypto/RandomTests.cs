using NBitcoin.Secp256k1;
using System;
using System.Collections.Generic;
using WalletWasabi.Crypto.Randomness;
using WalletWasabi.Helpers;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.Crypto
{
	public class RandomTests
	{
		[Fact]
		public void BasicTests()
		{
			// Make sure byte array comparison works within hashset and that the underlying API won't pull the floor out.
			var byteArray = new byte[] { 1, 2, 3 };
			var sameByteArray = new byte[] { 1, 2, 3 };
			var differentByteArray = new byte[] { 4, 5, 6 };
			var twoSet = new HashSet<byte[]>(new ByteArrayEqualityComparer())
			{
				byteArray,
				sameByteArray,
				differentByteArray
			};

			Assert.True(ByteHelpers.CompareFastUnsafe(byteArray, sameByteArray));
			Assert.False(ByteHelpers.CompareFastUnsafe(byteArray, differentByteArray));
			Assert.Equal(2, twoSet.Count);

			// It's probabilistically ensured that it never produces the same scalar, so unit test should pass always.
			var pseudoSet = new HashSet<byte[]>();
			var secureSet = new HashSet<byte[]>();
			var count = 100;
			var insecureRandom = new InsecureRandom();
			using var secureRandom = new SecureRandom();
			for (int i = 0; i < count; i++)
			{
				pseudoSet.Add(insecureRandom.GetBytes(10));
				secureSet.Add(secureRandom.GetBytes(10));
			}
			Assert.Equal(count, pseudoSet.Count);
			Assert.Equal(count, secureSet.Count);
		}

		[Fact]
		public void GetBytesArgumentTests()
		{
			var randoms = new List<WasabiRandom>
			{
				new SecureRandom(),
				new InsecureRandom(),
			};

			foreach (var random in randoms)
			{
				Assert.Throws<ArgumentOutOfRangeException>(() => random.GetBytes(int.MinValue));
				Assert.Throws<ArgumentOutOfRangeException>(() => random.GetBytes(-1));
				Assert.Throws<ArgumentOutOfRangeException>(() => random.GetBytes(0));

				var r1 = random.GetBytes(1);
				Assert.Single(r1);
				var r2 = random.GetBytes(2);
				Assert.Equal(2, r2.Length);
			}

			foreach (WasabiRandom random in randoms)
			{
				(random as IDisposable)?.Dispose();
			}
		}

		[Fact]
		public void ScalarTests()
		{
			// Make sure first that scalar equality works within hashset and that the underlying API won't pull the floor out.
			var singleSet = new HashSet<Scalar>
			{
				new Scalar(5),
				new Scalar(5)
			};
			Assert.Single(singleSet);

			var randoms = new List<WasabiRandom>
			{
				new SecureRandom(),
				new InsecureRandom()
			};

			foreach (var random in randoms)
			{
				// It's probabilistically ensured that it never produces the same scalar, so unit test should pass always.
				var set = new HashSet<Scalar>();
				var count = 100;
				for (int i = 0; i < count; i++)
				{
					Scalar randomScalar = random.GetScalar();
					set.Add(randomScalar);
				}
				Assert.Equal(count, set.Count);
			}

			foreach (WasabiRandom random in randoms)
			{
				(random as IDisposable)?.Dispose();
			}
		}

		[Fact]
		public void ScalarInternalTests()
		{
			var mockRandom = new MockRandom();

			// The random should not overflow.
			mockRandom.GetBytesResults.Add(EC.N.ToBytes());

			// The random should not overflow.
			mockRandom.GetBytesResults.Add(new Scalar(uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue).ToBytes());

			mockRandom.GetBytesResults.Add(Scalar.Zero.ToBytes());
			var one = new Scalar(1);
			mockRandom.GetBytesResults.Add(one.ToBytes());
			var two = new Scalar(2);
			mockRandom.GetBytesResults.Add(two.ToBytes());
			var big = new Scalar(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
			mockRandom.GetBytesResults.Add(big.ToBytes());
			var biggest = EC.N + one.Negate();
			mockRandom.GetBytesResults.Add(biggest.ToBytes());

			var randomScalar = mockRandom.GetScalar();
			Assert.Equal(Scalar.Zero, randomScalar);
			randomScalar = mockRandom.GetScalar();
			Assert.Equal(one, randomScalar);
			randomScalar = mockRandom.GetScalar();
			Assert.Equal(two, randomScalar);
			randomScalar = mockRandom.GetScalar();
			Assert.Equal(big, randomScalar);
			randomScalar = mockRandom.GetScalar();
			Assert.Equal(biggest, randomScalar);
		}

		[Fact]
		public void RandomStringTests()
		{
			var s1 = RandomString.AlphaNumeric(21, true);
			Assert.Equal(21, s1.Length);

			var s2 = RandomString.CapitalAlphaNumeric(21, false);
			Assert.Equal(21, s2.Length);
		}
	}
}

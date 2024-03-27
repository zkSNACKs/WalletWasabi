using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using WalletWasabi.Helpers;
using WalletWasabi.Microservices;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.BitcoinCore;

public class BitcoindBinaryHashesTests
{
	/// <summary>
	/// Bitcoin Knots distributes only SHA256 checksums for installers and for zip archives and not their content, so the validity of the hashes below depends on peer review consensus.
	/// </summary>
	/// <remarks>To verify a file hash, you can use, for example, <c>certUtil -hashfile bitcoind.exe SHA256</c> command.</remarks>
	[Fact]
	public void VerifyBitcoindBinaryChecksumHashes()
	{
		using var cts = new CancellationTokenSource(5_000);

		Dictionary<OSPlatform, string> expectedHashes = new()
		{
			{ OSPlatform.Windows, "55556074446894580a7c82f9420f3c93ea04a0d351f66f2cb9332362afcd6ac6" },
			{ OSPlatform.Linux, "742e9ef8e41c1c11f8515f74667d6c65a06e3bdf22bcaeff054d79034c668c21" },
			{ OSPlatform.OSX, "f9069f9b47b41df684074ffd4e5c31396be689f3fe295b95edb2b8fdb26cd016" },
		};

		foreach (var item in expectedHashes)
		{
			string binaryFolder = MicroserviceHelpers.GetBinaryFolder(item.Key);
			string filePath = Path.Combine(binaryFolder, item.Key == OSPlatform.Windows ? "bitcoind.exe" : "bitcoind");

			using SHA256 sha256 = SHA256.Create();
			using FileStream fileStream = File.OpenRead(filePath);
			Assert.Equal(item.Value, ByteHelpers.ToHex(sha256.ComputeHash(fileStream)).ToLowerInvariant());
		}
	}
}

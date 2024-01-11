using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using WalletWasabi.Helpers;
using WalletWasabi.Tor;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.Tor;

public class TorBinaryHashesTests
{
	[Fact(Skip = "TODO: Requires refactoring for mobile")]
	public void VerifyTorBinaryChecksumHashes()
	{
		Dictionary<OSPlatform, string> expectedHashes = new()
		{
			{ OSPlatform.Windows, "855799b771d166ac09e95ce99e77a219c35f1db3ed66342a2f224735c01a54bf" },
			{ OSPlatform.Linux, "79f1fe14e2c0d00cb604f21c0836ca58e5d5205d0a9a8acfb4a5df065492bf80" },
			{ OSPlatform.OSX, "ba028d74610083102c1f9cb95e6e746e3c0d374d61e275d3049729260900cb8b" },
		};

		using SHA256 sha256 = SHA256.Create();

		Dictionary<OSPlatform, string> actualHashes = new(capacity: expectedHashes.Count);

		foreach ((OSPlatform platform, string expectedHash) in expectedHashes)
		{
			string filePath = TorSettings.GetTorBinaryFilePath(platform);
			using FileStream fileStream = File.OpenRead(filePath);

			string actualHash = ByteHelpers.ToHex(sha256.ComputeHash(fileStream)).ToLowerInvariant();
			actualHashes.Add(platform, actualHash);
		}

		Assert.Equal(expectedHashes, actualHashes);
	}
}

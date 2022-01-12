using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WalletWasabi.Blockchain.TransactionBuilding;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.Blockchain.TransactionBuilding;

/// <summary>
/// Tests for <see cref="BranchAndBound"/> class.
/// </summary>
public class BranchAndBoundTests
{
	private static Random Random { get; } = new();

	[Fact]
	public void SimpleSelectionTest()
	{
		List<long> inputValues = new() { 120_000, 100_000, 100_000, 50_000, 40_000 };
		BranchAndBound selector = new(inputValues);
		List<long> expectedValues = new() { 40_000, 50_000, 100_000 };
		long target = 190_000;

		bool wasSuccessful = selector.TryGetExactMatch(target, out List<long>? selectedCoins, CancellationToken.None);

		Assert.True(wasSuccessful);
		Assert.NotNull(selectedCoins);
		Assert.Equal(expectedValues, selectedCoins);
	}

	[Fact]
	public void CanSelectEveryCoin()
	{
		List<long> inputValues = new() { 120_000, 100_000, 100_000 };
		BranchAndBound selector = new(inputValues);
		List<long> expectedValues = new() { 100_000, 100_000, 120_000 };
		long target = 320000;

		bool wasSuccessful = selector.TryGetExactMatch(target, out List<long>? selectedCoins, CancellationToken.None);

		Assert.True(wasSuccessful);
		Assert.NotNull(selectedCoins);
		Assert.Equal(expectedValues, selectedCoins);
	}

	/// <summary>
	/// Tests that sum of input values must be larger or equal to the target otherwise we end up searching all options in vain.
	/// </summary>
	[Fact]
	public void TargetIsBiggerThanBalance()
	{
		long target = 5_000;
		List<long> inputValues = new();

		for (int i = 0; i < target - 1; i++)
		{
			inputValues.Add(1);
		}

		BranchAndBound selector = new(inputValues);
		bool wasSuccessful = selector.TryGetExactMatch(target, out List<long>? selectedCoins, CancellationToken.None);

		Assert.False(wasSuccessful);
		Assert.Null(selectedCoins);
	}

	[Fact]
	public void ReturnNullIfNoExactMatchFoundTest()
	{
		List<long> inputValues = new() { 120_000, 100_000, 100_000, 50_000, 40_000 };
		BranchAndBound selector = new(inputValues);
		long target = 300000;

		bool wasSuccessful = selector.TryGetExactMatch(target, out List<long>? selectedCoins, CancellationToken.None);

		Assert.False(wasSuccessful);
		Assert.Null(selectedCoins);
	}

	[Fact]
	public void CanSelectCoinsWithToleranceTest()
	{
		List<long> inputValues = new() { 1200, 1000, 1100 };
		BranchAndBound selector = new(inputValues);
		long target = 3200;

		bool wasSuccessful = selector.TryGetExactMatch(target, out List<long>? selectedCoins, CancellationToken.None);

		Assert.True(wasSuccessful);
		Assert.NotNull(selectedCoins);
	}
}

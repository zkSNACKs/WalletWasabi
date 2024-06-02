using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models;
using WalletWasabi.Fluent.ViewModels.Wallets.Send;
using WalletWasabi.Tests.Helpers;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.UserInterfaceTest;

public class PocketSelectionTests
{
	private LabelSelectionViewModel CreateLabelSelectionViewModel(Money amount, LabelsArray recipient)
	{
		var pw = "";
		var km = KeyManager.Recover(
			new Mnemonic("all all all all all all all all all all all all"),
			pw,
			Network.Main,
			KeyManager.GetAccountKeyPath(Network.Main, ScriptPubKeyType.Segwit));
		var address = BitcoinAddress.Create("bc1q7v7qfhwx55erxkc66nsv39x4azwufvy6zq8ya4", Network.Main);
		var info = new TransactionInfo(address, 100)
		{
			Amount = amount,
			FeeRate = new FeeRate(2m),
			Recipient = recipient
		};

		return new LabelSelectionViewModel(km, pw, info, isSilent: false);
	}

	[Fact]
	public async Task WhiteListHighlightsAllLabelsInOtherPocketsThatContainTargetLabelAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out _, "Target", "Dan", "Roland");
		pockets.AddPocket(1.0M, out _, "Target");
		pockets.AddPocket(1.0M, out _, "David", "Adam", "Lucas");
		pockets.AddPocket(1.0M, out _, "Jumar");

		await selection.ResetAsync(pockets.ToArray());

		selection.GetLabel("Target").IsPointerOver = true;

		Assert.True(selection.GetLabel("Target").IsHighlighted);
		Assert.True(selection.GetLabel("Dan").IsHighlighted);
		Assert.True(selection.GetLabel("Roland").IsHighlighted);

		Assert.False(selection.GetLabel("David").IsHighlighted);
		Assert.False(selection.GetLabel("Adam").IsHighlighted);
		Assert.False(selection.GetLabel("Lucas").IsHighlighted);
		Assert.False(selection.GetLabel("Jumar").IsHighlighted);
	}

	[Fact]
	public async Task AllWhitelistPocketsAreOutputAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out var pocket1, "Target", "Dan", "Roland");
		pockets.AddPocket(1.0M, out var pocket2, "Target");
		pockets.AddPocket(1.0M, out var pocket3, "David", "Adam", "Lucas");
		pockets.AddPocket(1.0M, out var pocket4, "Jumar");

		await selection.ResetAsync(pockets.ToArray());

		var output = selection.GetUsedPockets();

		Assert.Contains(pocket1, output);
		Assert.Contains(pocket2, output);
		Assert.Contains(pocket3, output);
		Assert.Contains(pocket4, output);
	}

	[Fact]
	public async Task WhiteListHighlightsAllLabelsInOtherPocketsThatContainTargetLabelExceptThoseAvailableInOtherPocketsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out _, "Target", "Dan", "Roland");
		pockets.AddPocket(1.0M, out _, "Dan");

		await selection.ResetAsync(pockets.ToArray());

		selection.GetLabel("Target").IsPointerOver = true;

		Assert.True(selection.GetLabel("Target").IsHighlighted);
		Assert.True(selection.GetLabel("Roland").IsHighlighted);
		Assert.False(selection.GetLabel("Dan").IsHighlighted);
	}

	[Fact]
	public async Task WhiteListHighlightsAllLabelsInOtherPocketsThatContainTargetLabelAndTheOtherLabelsInThosePocketsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out _, "Target", "Dan", "Roland");
		pockets.AddPocket(1.0M, out _, "Dan");

		await selection.ResetAsync(pockets.ToArray());

		selection.GetLabel("Dan").IsPointerOver = true;

		Assert.True(selection.GetLabel("Target").IsHighlighted);
		Assert.True(selection.GetLabel("Roland").IsHighlighted);
		Assert.True(selection.GetLabel("Dan").IsHighlighted);
	}

	[Fact]
	public async Task WhiteListSwapsAllLabelsInOtherPocketsThatContainTargetLabelAndTheOtherLabelsInThosePocketsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out _, "Target", "Dan", "Roland");
		pockets.AddPocket(1.0M, out _, "Dan");

		await selection.ResetAsync(pockets.ToArray());

		await selection.SwapLabelAsync(selection.GetLabel("Dan"));

		Assert.Contains(selection.GetLabel("Target"), selection.LabelsBlackList);
		Assert.Contains(selection.GetLabel("Roland"), selection.LabelsBlackList);
		Assert.Contains(selection.GetLabel("Dan"), selection.LabelsBlackList);
	}

	[Fact]
	public async Task OutPutMatchesWhiteListScenario1Async()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out var pocket1, "Target", "Dan", "Roland");
		pockets.AddPocket(1.0M, out var pocket2, "Dan");

		await selection.ResetAsync(pockets.ToArray());

		await selection.SwapLabelAsync(selection.GetLabel("Dan"));

		var output = selection.GetUsedPockets();

		Assert.DoesNotContain(pocket1, output);
		Assert.DoesNotContain(pocket2, output);
	}

	[Fact]
	public async Task BlackListHighlightsLabelsExclusivelyThatExistInMultiplePocketsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out var pocket1, "Target", "Dan", "Roland");
		pockets.AddPocket(1.0M, out var pocket2, "Dan");

		await selection.ResetAsync(pockets.ToArray());

		await selection.SwapLabelAsync(selection.GetLabel("Dan"));

		Assert.Contains(selection.GetLabel("Target"), selection.LabelsBlackList);
		Assert.Contains(selection.GetLabel("Roland"), selection.LabelsBlackList);
		Assert.Contains(selection.GetLabel("Dan"), selection.LabelsBlackList);

		selection.GetLabel("Dan").IsPointerOver = true;

		Assert.True(selection.GetLabel("Dan").IsHighlighted);
		Assert.False(selection.GetLabel("Roland").IsHighlighted);
		Assert.False(selection.GetLabel("Target").IsHighlighted);

		var output = selection.GetUsedPockets();

		Assert.DoesNotContain(pocket1, output);
		Assert.DoesNotContain(pocket2, output);
	}

	[Fact]
	public async Task BlackListHighlightsDealWithMultipleOverlapsCorrectlyAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out var pocket1, "Target", "Dan");
		pockets.AddPocket(1.0M, out var pocket2, "Target");
		pockets.AddPocket(1.0M, out var pocket3, "Target", "Roland");

		await selection.ResetAsync(pockets.ToArray());

		await selection.SwapLabelAsync(selection.GetLabel("Target"));

		Assert.Contains(selection.GetLabel("Target"), selection.LabelsBlackList);
		Assert.Contains(selection.GetLabel("Roland"), selection.LabelsBlackList);
		Assert.Contains(selection.GetLabel("Dan"), selection.LabelsBlackList);

		selection.GetLabel("Dan").IsPointerOver = true;

		Assert.True(selection.GetLabel("Dan").IsHighlighted);
		Assert.False(selection.GetLabel("Roland").IsHighlighted);
		Assert.True(selection.GetLabel("Target").IsHighlighted);

		var output = selection.GetUsedPockets();

		Assert.DoesNotContain(pocket1, output);
		Assert.DoesNotContain(pocket2, output);
		Assert.DoesNotContain(pocket3, output);
	}

	[Fact]
	public async Task WhiteListHighlightsDealWithMultipleOverlapsCorrectlyAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out _, "Target", "Dan");
		pockets.AddPocket(1.0M, out _, "Target");
		pockets.AddPocket(1.0M, out _, "Target", "Roland");

		await selection.ResetAsync(pockets.ToArray());

		selection.GetLabel("Dan").IsPointerOver = true;

		Assert.True(selection.GetLabel("Dan").IsHighlighted);
		Assert.False(selection.GetLabel("Roland").IsHighlighted);
		Assert.False(selection.GetLabel("Target").IsHighlighted);
	}

	[Fact]
	public async Task WhiteListSwapsGroupedLabelsInOtherPocketsThatContainTargetLabelExceptThoseAvailableInOtherPocketsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out var pocket1, "Target", "Dan");
		pockets.AddPocket(1.0M, out var pocket2, "Target");
		pockets.AddPocket(1.0M, out var pocket3, "Target", "Roland");

		await selection.ResetAsync(pockets.ToArray());

		await selection.SwapLabelAsync(selection.GetLabel("Dan"));

		Assert.DoesNotContain(selection.GetLabel("Target"), selection.LabelsBlackList);
		Assert.DoesNotContain(selection.GetLabel("Roland"), selection.LabelsBlackList);
		Assert.Contains(selection.GetLabel("Dan"), selection.LabelsBlackList);

		var output = selection.GetUsedPockets();

		Assert.DoesNotContain(pocket1, output);
		Assert.Contains(pocket2, output);
		Assert.Contains(pocket3, output);
	}

	[Fact]
	public async Task MovesFromWhiteListHighlightsAllLabelsInOtherPocketsThatContainTargetLabelAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out _, "Target", "Dan", "Roland");
		pockets.AddPocket(1.0M, out _, "Target");
		pockets.AddPocket(1.0M, out _, "David", "Adam", "Lucas");
		pockets.AddPocket(1.0M, out _, "Jumar");

		await selection.ResetAsync(pockets.ToArray());

		await selection.SwapLabelAsync(selection.GetLabel("Target"));

		Assert.Contains(selection.GetLabel("Target"), selection.LabelsBlackList);
		Assert.Contains(selection.GetLabel("Dan"), selection.LabelsBlackList);
		Assert.Contains(selection.GetLabel("Roland"), selection.LabelsBlackList);

		Assert.DoesNotContain(selection.GetLabel("David"), selection.LabelsBlackList);
		Assert.DoesNotContain(selection.GetLabel("Adam"), selection.LabelsBlackList);
		Assert.DoesNotContain(selection.GetLabel("Lucas"), selection.LabelsBlackList);
		Assert.DoesNotContain(selection.GetLabel("Jumar"), selection.LabelsBlackList);

		Assert.DoesNotContain(selection.GetLabel("Target"), selection.LabelsWhiteList);
		Assert.DoesNotContain(selection.GetLabel("Dan"), selection.LabelsWhiteList);
		Assert.DoesNotContain(selection.GetLabel("Roland"), selection.LabelsWhiteList);

		Assert.Contains(selection.GetLabel("David"), selection.LabelsWhiteList);
		Assert.Contains(selection.GetLabel("Adam"), selection.LabelsWhiteList);
		Assert.Contains(selection.GetLabel("Lucas"), selection.LabelsWhiteList);
		Assert.Contains(selection.GetLabel("Jumar"), selection.LabelsWhiteList);
	}

	[Fact]
	public async Task PrivateAndSemiPrivatePocketsAreHiddenAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(2.0M, out var pocket2, "Dan");
		pockets.AddPocket(1.0M, out var pocket3, CoinPocketHelper.SemiPrivateFundsText);

		await selection.ResetAsync(pockets.ToArray());

		Assert.Contains(selection.GetLabel("Dan"), selection.LabelsWhiteList);

		// Not found.
		Assert.Throws<InvalidOperationException>(() => selection.GetLabel(CoinPocketHelper.PrivateFundsText));
		Assert.Throws<InvalidOperationException>(() => selection.GetLabel(CoinPocketHelper.SemiPrivateFundsText));
	}

	[Fact]
	public async Task UseOnlyPrivateFundsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.1M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(2.0M, out var pocket2, "Dan");
		pockets.AddPocket(1.0M, out var pocket3, CoinPocketHelper.SemiPrivateFundsText);

		await selection.ResetAsync(pockets.ToArray());

		await selection.SwapLabelAsync(selection.GetLabel("Dan"));

		Assert.Contains(selection.GetLabel("Dan"), selection.LabelsBlackList);
		Assert.True(selection.EnoughSelected);

		var output = selection.GetUsedPockets();
		Assert.DoesNotContain(pocket2, output);
		Assert.DoesNotContain(pocket3, output);
		Assert.Contains(pocket1, output);
	}

	[Fact]
	public async Task UsePrivateAndSemiPrivateFundsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.5"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(0.9M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(2.0M, out var pocket2, "Dan");
		pockets.AddPocket(1.0M, out var pocket3, CoinPocketHelper.SemiPrivateFundsText);

		await selection.ResetAsync(pockets.ToArray());

		await selection.SwapLabelAsync(selection.GetLabel("Dan"));

		Assert.Contains(selection.GetLabel("Dan"), selection.LabelsBlackList);
		Assert.True(selection.EnoughSelected);

		var output = selection.GetUsedPockets();
		Assert.Contains(pocket1, output);
		Assert.DoesNotContain(pocket2, output);
		Assert.Contains(pocket3, output);
	}

	[Fact]
	public async Task DoNotUsePrivateFundsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), "Dan");

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(2.0M, out var pocket2, "Dan");
		pockets.AddPocket(1.0M, out var pocket3, CoinPocketHelper.SemiPrivateFundsText);

		await selection.ResetAsync(pockets.ToArray());

		Assert.Contains(selection.GetLabel("Dan"), selection.LabelsWhiteList);
		Assert.True(selection.EnoughSelected);

		var output = selection.GetUsedPockets();
		Assert.DoesNotContain(pocket1, output);
		Assert.Contains(pocket2, output);
		Assert.DoesNotContain(pocket3, output);
	}

	[Fact]
	public async Task IncludePrivateFundsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("2.5"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(2.0M, out var pocket2, "Dan");
		pockets.AddPocket(1.0M, out var pocket3, CoinPocketHelper.SemiPrivateFundsText);

		await selection.ResetAsync(pockets.ToArray());

		Assert.True(selection.EnoughSelected);

		var output = selection.GetUsedPockets();
		Assert.Contains(pocket1, output);
		Assert.Contains(pocket2, output);
		Assert.DoesNotContain(pocket3, output);
	}

	[Fact]
	public async Task IncludePrivateAndSemiPrivateFundsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("2.5"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(0.6M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(1.0M, out var pocket2, "Dan");
		pockets.AddPocket(1.0M, out var pocket3, CoinPocketHelper.SemiPrivateFundsText);

		await selection.ResetAsync(pockets.ToArray());

		Assert.True(selection.EnoughSelected);

		var output = selection.GetUsedPockets();
		Assert.Contains(pocket1, output);
		Assert.Contains(pocket2, output);
		Assert.Contains(pocket3, output);
	}

	[Fact]
	public async Task StillIncludePrivateFundsAfterSwapAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), LabelsArray.Empty);
		var pockets = new List<Pocket>();

		var privateCoin = LabelTestExtensions.CreateCoin(0.8m, "", 999);
		var privatePocket = new Pocket((CoinPocketHelper.PrivateFundsText, new CoinsView(new[] { privateCoin })));
		pockets.Add(privatePocket);

		pockets.AddPocket(0.3M, out var pocket2, "Dan");
		pockets.AddPocket(0.1M, out var pocket3, "Lucas");

		await selection.ResetAsync(pockets.ToArray());

		var usedCoins = new List<SmartCoin>
		{
			privateCoin
		};
		usedCoins.AddRange(pocket2.Coins);

		await selection.SetUsedLabelAsync(usedCoins, 10);
		var output = selection.GetUsedPockets();
		Assert.Contains(privatePocket, output);
		Assert.Contains(pocket2, output);
		Assert.DoesNotContain(pocket3, output);
		Assert.True(selection.EnoughSelected);

		await selection.SwapLabelAsync(selection.GetLabel("Lucas"));
		await selection.SwapLabelAsync(selection.GetLabel("Lucas"));
		Assert.True(selection.EnoughSelected);
	}

	[Fact]
	public async Task NotEnoughSelectedAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out _, "David");
		pockets.AddPocket(2.0M, out _, "Dan");

		await selection.ResetAsync(pockets.ToArray());

		await selection.SwapLabelAsync(selection.GetLabel("David"));
		await selection.SwapLabelAsync(selection.GetLabel("Dan"));

		Assert.False(selection.EnoughSelected);
	}

	[Fact]
	public async Task AutoSelectOnlyPrivatePocketAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("0.7"), "Dan");

		var pockets = new List<Pocket>();
		pockets.AddPocket(0.8M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(0.6M, out var pocket2, "Dan");
		pockets.AddPocket(0.7M, out var pocket3, CoinPocketHelper.UnlabelledFundsText);
		pockets.AddPocket(0.7M, out var pocket4, CoinPocketHelper.SemiPrivateFundsText);

		await selection.ResetAsync(pockets.ToArray());

		var output = await selection.AutoSelectPocketsAsync();
		Assert.Contains(pocket1, output);
		Assert.DoesNotContain(pocket2, output);
		Assert.DoesNotContain(pocket3, output);
		Assert.DoesNotContain(pocket4, output);
	}

	[Fact]
	public async Task AutoSelectPrivateAndSemiPrivatePocketsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("0.7"), "Dan");

		var pockets = new List<Pocket>();
		pockets.AddPocket(0.4M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(0.6M, out var pocket2, "Dan");
		pockets.AddPocket(0.7M, out var pocket3, CoinPocketHelper.UnlabelledFundsText);
		pockets.AddPocket(0.4M, out var pocket4, CoinPocketHelper.SemiPrivateFundsText);

		await selection.ResetAsync(pockets.ToArray());

		var output = await selection.AutoSelectPocketsAsync();
		Assert.Contains(pocket1, output);
		Assert.DoesNotContain(pocket2, output);
		Assert.DoesNotContain(pocket3, output);
		Assert.Contains(pocket4, output);
	}

	[Fact]
	public async Task AutoSelectPrivateAndSemiPrivateAndKnownPocketsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), "Dan");

		var pockets = new List<Pocket>();
		pockets.AddPocket(0.3M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(0.5M, out var pocket2, "Dan");
		pockets.AddPocket(0.4M, out var pocket3, CoinPocketHelper.UnlabelledFundsText);
		pockets.AddPocket(0.3M, out var pocket4, CoinPocketHelper.SemiPrivateFundsText);

		await selection.ResetAsync(pockets.ToArray());

		var output = await selection.AutoSelectPocketsAsync();
		Assert.Contains(pocket1, output);
		Assert.Contains(pocket2, output);
		Assert.DoesNotContain(pocket3, output);
		Assert.Contains(pocket4, output);
	}

	[Fact]
	public async Task AutoSelectPrivateAndSemiPrivateAndUnknownPocketsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), "Dan");

		var pockets = new List<Pocket>();
		pockets.AddPocket(0.3M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(0.1M, out var pocket2, "Dan");
		pockets.AddPocket(0.5M, out var pocket3, CoinPocketHelper.UnlabelledFundsText);
		pockets.AddPocket(0.4M, out var pocket4, CoinPocketHelper.SemiPrivateFundsText);

		await selection.ResetAsync(pockets.ToArray());

		var output = await selection.AutoSelectPocketsAsync();
		Assert.Contains(pocket1, output);
		Assert.DoesNotContain(pocket2, output);
		Assert.Contains(pocket3, output);
		Assert.Contains(pocket4, output);
	}

	[Fact]
	public async Task AutoSelectAllPocketsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("2.0"), "Dan");

		var pockets = new List<Pocket>();
		pockets.AddPocket(0.8M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(0.3M, out var pocket2, "Dan");
		pockets.AddPocket(0.5M, out var pocket3, CoinPocketHelper.UnlabelledFundsText);
		pockets.AddPocket(0.5M, out var pocket4, CoinPocketHelper.SemiPrivateFundsText);

		await selection.ResetAsync(pockets.ToArray());

		var output = await selection.AutoSelectPocketsAsync();
		Assert.Contains(pocket1, output);
		Assert.Contains(pocket2, output);
		Assert.Contains(pocket3, output);
		Assert.Contains(pocket4, output);
	}

	[Fact]
	public async Task AutoSelectOnlyKnownByRecipientPocketsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), "David, Lucas");

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(1.1M, out var pocket2, "Dan");
		pockets.AddPocket(0.5M, out var pocket3, CoinPocketHelper.UnlabelledFundsText);
		pockets.AddPocket(1.1M, out var pocket4, "David", "Lucas");
		pockets.AddPocket(1.1M, out var pocket5, "David");
		pockets.AddPocket(1.1M, out var pocket6, "Lucas");
		pockets.AddPocket(1.1M, out var pocket7, "David", "Lucas", "Dan");
		pockets.AddPocket(1.0M, out var pocket8, CoinPocketHelper.SemiPrivateFundsText);

		await selection.ResetAsync(pockets.ToArray());

		var output = await selection.AutoSelectPocketsAsync();
		Assert.DoesNotContain(pocket1, output);
		Assert.DoesNotContain(pocket2, output);
		Assert.DoesNotContain(pocket3, output);
		Assert.Contains(pocket4, output);
		Assert.DoesNotContain(pocket5, output);
		Assert.DoesNotContain(pocket6, output);
		Assert.DoesNotContain(pocket7, output);
		Assert.DoesNotContain(pocket8, output);
	}

	[Fact]
	public async Task AutoSelectOnlyKnownByRecipientPocketsCaseInsensitiveAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), "dAN");

		var pockets = new List<Pocket>();
		pockets.AddPocket(2.8M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(1.1M, out var pocket2, "Dan");
		pockets.AddPocket(1.1M, out var pocket3, "Lucas", "Dan");
		pockets.AddPocket(1.1M, out var pocket4, "dan");

		await selection.ResetAsync(pockets.ToArray());

		var output = await selection.AutoSelectPocketsAsync();
		Assert.DoesNotContain(pocket1, output);
		Assert.Contains(pocket2, output);
		Assert.DoesNotContain(pocket3, output);
		Assert.Contains(pocket4, output);
	}

	[Fact]
	public async Task AutoSelectKnownByRecipientPocketsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.9"), "David");

		var pockets = new List<Pocket>();
		pockets.AddPocket(0.8M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(1.1M, out var pocket2, "Dan");
		pockets.AddPocket(1.5M, out var pocket3, CoinPocketHelper.UnlabelledFundsText);
		pockets.AddPocket(1.1M, out var pocket4, "David", "Lucas");
		pockets.AddPocket(0.1M, out var pocket5, CoinPocketHelper.SemiPrivateFundsText);

		await selection.ResetAsync(pockets.ToArray());

		var output = await selection.AutoSelectPocketsAsync();
		Assert.Contains(pocket1, output);
		Assert.DoesNotContain(pocket2, output);
		Assert.DoesNotContain(pocket3, output);
		Assert.Contains(pocket4, output);
		Assert.Contains(pocket5, output);
	}

	[Fact]
	public async Task AutoSelectKnownByMultipleRecipientPocketsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.9"), "David, Lucas");

		var pockets = new List<Pocket>();
		pockets.AddPocket(0.8M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(1.1M, out var pocket2, "Dan");
		pockets.AddPocket(0.5M, out var pocket3, CoinPocketHelper.UnlabelledFundsText);
		pockets.AddPocket(1.1M, out var pocket4, "David", "Lucas", "Dan");
		pockets.AddPocket(1.1M, out var pocket5, "David");
		pockets.AddPocket(1.1M, out var pocket6, "Lucas");
		pockets.AddPocket(1.1M, out var pocket7, "David", "Lucas", "Dan", "Roland");
		pockets.AddPocket(1.1M, out var pocket8, "David", "Dan");
		pockets.AddPocket(0.1M, out var pocket9, CoinPocketHelper.SemiPrivateFundsText);

		await selection.ResetAsync(pockets.ToArray());

		var output = await selection.AutoSelectPocketsAsync();
		Assert.Contains(pocket1, output);
		Assert.DoesNotContain(pocket2, output);
		Assert.DoesNotContain(pocket3, output);
		Assert.Contains(pocket4, output);
		Assert.DoesNotContain(pocket5, output);
		Assert.DoesNotContain(pocket6, output);
		Assert.DoesNotContain(pocket7, output);
		Assert.DoesNotContain(pocket8, output);
		Assert.Contains(pocket9, output);
	}

	[Fact]
	public async Task AutoSelectMultipleKnownByMultipleRecipientPocketsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), "David, Lucas");

		var pockets = new List<Pocket>();
		pockets.AddPocket(0.2M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(1.1M, out var pocket2, "Dan");
		pockets.AddPocket(0.5M, out var pocket3, CoinPocketHelper.UnlabelledFundsText);
		pockets.AddPocket(0.4M, out var pocket4, "David", "Lucas", "Dan");
		pockets.AddPocket(0.6M, out var pocket5, "David");
		pockets.AddPocket(0.5M, out var pocket6, "Lucas");
		pockets.AddPocket(0.5M, out var pocket7, "David", "Lucas", "Dan", "Roland");
		pockets.AddPocket(0.5M, out var pocket8, "David", "Dan");
		pockets.AddPocket(0.1M, out var pocket9, CoinPocketHelper.SemiPrivateFundsText);

		await selection.ResetAsync(pockets.ToArray());

		var output = await selection.AutoSelectPocketsAsync();
		Assert.Contains(pocket1, output);
		Assert.DoesNotContain(pocket2, output);
		Assert.DoesNotContain(pocket3, output);
		Assert.Contains(pocket4, output);
		Assert.Contains(pocket5, output);
		Assert.DoesNotContain(pocket6, output);
		Assert.DoesNotContain(pocket7, output);
		Assert.DoesNotContain(pocket8, output);
		Assert.Contains(pocket9, output);
	}

	[Fact]
	public async Task AutoSelectRequiredKnownByRecipientPocketsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.3"), "David");

		var pockets = new List<Pocket>();
		pockets.AddPocket(0.2M, out var pocket1, CoinPocketHelper.PrivateFundsText);
		pockets.AddPocket(1.1M, out var pocket2, "Dan");
		pockets.AddPocket(1.5M, out var pocket3, CoinPocketHelper.UnlabelledFundsText);
		pockets.AddPocket(0.5M, out var pocket4, "David");
		pockets.AddPocket(0.4M, out var pocket5, "David", "Max");
		pockets.AddPocket(0.6M, out var pocket6, "David", "Lucas", "Dan");
		pockets.AddPocket(0.1M, out var pocket7, CoinPocketHelper.SemiPrivateFundsText);

		await selection.ResetAsync(pockets.ToArray());

		var output = await selection.AutoSelectPocketsAsync();
		Assert.Contains(pocket1, output);
		Assert.DoesNotContain(pocket2, output);
		Assert.DoesNotContain(pocket3, output);
		Assert.Contains(pocket4, output);
		Assert.DoesNotContain(pocket5, output);
		Assert.Contains(pocket6, output);
		Assert.Contains(pocket7, output);
	}

	[Fact]
	public async Task NotEnoughSelectedWhenSameLabelFoundInSeveralPocketsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), LabelsArray.Empty);

		var pockets = new List<Pocket>();
		pockets.AddPocket(0.4M, out _, "Dan");
		pockets.AddPocket(2.0M, out _, "Dan", "David");

		await selection.ResetAsync(pockets.ToArray());

		await selection.SwapLabelAsync(selection.GetLabel("Dan"));
		await selection.SwapLabelAsync(selection.GetLabel("Dan"));

		Assert.DoesNotContain(selection.GetLabel("Dan"), selection.LabelsBlackList);
		Assert.DoesNotContain(selection.GetLabel("David"), selection.LabelsWhiteList);

		Assert.False(selection.EnoughSelected);
	}

	[Fact]
	public async Task SetUsedLabelIgnoreCaseAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.0"), "Dan");

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.2M, out var pocket1, "Dan");
		pockets.AddPocket(1.2M, out var pocket2, "Lucas");

		await selection.ResetAsync(pockets.ToArray());

		var output = await selection.AutoSelectPocketsAsync();
		Assert.Contains(pocket1, output);
		Assert.DoesNotContain(pocket2, output);

		var hdpk = LabelTestExtensions.NewKey("dan");
		var usedCoin = BitcoinFactory.CreateSmartCoin(hdpk, 1.0M);
		await selection.SetUsedLabelAsync(new[] { usedCoin }, privateThreshold: 10);

		Assert.Contains(selection.GetLabel("Lucas"), selection.LabelsBlackList);
		Assert.Contains(selection.GetLabel("Dan"), selection.LabelsWhiteList);
	}

	[Fact]
	public async Task SetUsedLabelIncludePrivateFundsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.5"), "Dan");

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out _, "Dan");

		var privateCoins = new[]
		{
			BitcoinFactory.CreateSmartCoin(LabelTestExtensions.NewKey(anonymitySet: 999), 0.5m),
			BitcoinFactory.CreateSmartCoin(LabelTestExtensions.NewKey(anonymitySet: 999), 0.5m),
		};
		var coinsView = new CoinsView(privateCoins.ToArray());
		var pocket = new Pocket((LabelsArray.Empty, coinsView));
		pockets.Add(pocket);

		await selection.ResetAsync(pockets.ToArray());

		var output = await selection.AutoSelectPocketsAsync();

		await selection.SetUsedLabelAsync(output.SelectMany(x => x.Coins), privateThreshold: 10);

		Assert.True(selection.EnoughSelected);
	}

	[Fact]
	public async Task SetUsedLabelIncludeSemiPrivateFundsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("1.5"), "Dan");

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out _, "Dan");

		var semiPrivateCoins = new[]
		{
			BitcoinFactory.CreateSmartCoin(LabelTestExtensions.NewKey(anonymitySet: 5), 0.5m),
			BitcoinFactory.CreateSmartCoin(LabelTestExtensions.NewKey(anonymitySet: 5), 0.5m),
		};
		var coinsView = new CoinsView(semiPrivateCoins.ToArray());
		var pocket = new Pocket((LabelsArray.Empty, coinsView));
		pockets.Add(pocket);

		await selection.ResetAsync(pockets.ToArray());

		var output = await selection.AutoSelectPocketsAsync();

		await selection.SetUsedLabelAsync(output.SelectMany(x => x.Coins), privateThreshold: 10);

		Assert.True(selection.EnoughSelected);
	}

	[Fact]
	public async Task SetUsedLabelIncludePrivateAndSemiPrivateFundsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("2.5"), "Dan");

		var pockets = new List<Pocket>();
		pockets.AddPocket(1.0M, out _, "Dan");

		var privateCoins = new[]
		{
			BitcoinFactory.CreateSmartCoin(LabelTestExtensions.NewKey(anonymitySet: 999), 0.5m),
			BitcoinFactory.CreateSmartCoin(LabelTestExtensions.NewKey(anonymitySet: 999), 0.5m),
		};
		var privateCoinsView = new CoinsView(privateCoins.ToArray());
		var privatePocket = new Pocket((LabelsArray.Empty, privateCoinsView));
		pockets.Add(privatePocket);

		var semiPrivateCoins = new[]
		{
			BitcoinFactory.CreateSmartCoin(LabelTestExtensions.NewKey(anonymitySet: 5), 0.5m),
			BitcoinFactory.CreateSmartCoin(LabelTestExtensions.NewKey(anonymitySet: 5), 0.5m),
		};
		var semiPrivateCoinsView = new CoinsView(semiPrivateCoins.ToArray());
		var semiPrivatePocket = new Pocket((LabelsArray.Empty, semiPrivateCoinsView));
		pockets.Add(semiPrivatePocket);

		await selection.ResetAsync(pockets.ToArray());

		var output = await selection.AutoSelectPocketsAsync();

		await selection.SetUsedLabelAsync(output.SelectMany(x => x.Coins), privateThreshold: 10);

		Assert.True(selection.EnoughSelected);
	}

	[Fact]
	public async Task IsPocketEnoughWithoutCoinjoiningCoinsAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("0.5"), "Daniel");

		var pockets = new List<Pocket>();
		var privateCoins = new[]
		{
			BitcoinFactory.CreateSmartCoin(LabelTestExtensions.NewKey(anonymitySet: 999), 1m),
			BitcoinFactory.CreateSmartCoin(LabelTestExtensions.NewKey(anonymitySet: 999), 1m),
			BitcoinFactory.CreateSmartCoin(LabelTestExtensions.NewKey(anonymitySet: 999), 1m)
		};
		var privateCoinsView = new CoinsView(privateCoins.ToArray());
		var privatePocket = new Pocket((LabelsArray.Empty, privateCoinsView));
		pockets.Add(privatePocket);

		var coinjoiningCoins = new List<SmartCoin>() { privateCoins[0], privateCoins[1] };
		var excludedCoinsView = new CoinsView(coinjoiningCoins.ToArray());

		await selection.ResetAsync(pockets.ToArray(), coinsToExclude: coinjoiningCoins);

		var output = await selection.AutoSelectPocketsAsync();

		var selectedCoins = output.SelectMany(pocket => pocket.Coins);

		Assert.True(selection.EnoughSelected);
		Assert.Empty(selectedCoins.Intersect(excludedCoinsView));
		Assert.DoesNotContain(privateCoins[0], selectedCoins);
		Assert.DoesNotContain(privateCoins[1], selectedCoins);
		Assert.Contains(privateCoins[2], selectedCoins);
	}

	[Fact]
	public async Task UseCoinjoiningCoinsIfNecessaryAsync()
	{
		var selection = CreateLabelSelectionViewModel(Money.Parse("2.9"), "Daniel");

		var pockets = new List<Pocket>();
		var privateCoins = new[]
		{
			BitcoinFactory.CreateSmartCoin(LabelTestExtensions.NewKey(anonymitySet: 999), 1m),
			BitcoinFactory.CreateSmartCoin(LabelTestExtensions.NewKey(anonymitySet: 999), 1m),
			BitcoinFactory.CreateSmartCoin(LabelTestExtensions.NewKey(anonymitySet: 999), 1m)
		};

		var privateCoinsView = new CoinsView(privateCoins.ToArray());
		var privatePocket = new Pocket((LabelsArray.Empty, privateCoinsView));
		pockets.Add(privatePocket);

		var coinjoiningCoins = new List<SmartCoin>() { privateCoins[0], privateCoins[1] };
		var excludedCoinsView = new CoinsView(coinjoiningCoins.ToArray());

		await selection.ResetAsync(pockets.ToArray(), coinsToExclude: coinjoiningCoins);

		var output = await selection.AutoSelectPocketsAsync();

		var selectedCoins = output.SelectMany(pocket => pocket.Coins);

		Assert.True(selection.EnoughSelected);
		Assert.Contains(privateCoins[0], selectedCoins);
		Assert.Contains(privateCoins[1], selectedCoins);
		Assert.Contains(privateCoins[2], selectedCoins);
	}

	[Fact]
	public async Task IsOtherSelectionPossibleCasesAsync()
	{
		var pockets = new List<Pocket>();

		var privatePocket = LabelTestExtensions.CreateSingleCoinPocket(1.0m, CoinPocketHelper.PrivateFundsText, anonSet: 999);
		var semiPrivatePocket = LabelTestExtensions.CreateSingleCoinPocket(1.0m, CoinPocketHelper.SemiPrivateFundsText, anonSet: 2);

		pockets.Add(LabelTestExtensions.CreateSingleCoinPocket(1.0m, "Dan"));
		pockets.Add(LabelTestExtensions.CreateSingleCoinPocket(1.0m, "Dan, Lucas"));

		// Other pocket cannot be used case.
		var recipient = "Lucas";
		var selection = CreateLabelSelectionViewModel(Money.Parse("0.5"), recipient);
		await selection.ResetAsync(pockets.ToArray());
		var output = await selection.AutoSelectPocketsAsync();
		Assert.False(selection.IsOtherSelectionPossible(output.SelectMany(x => x.Coins), recipient));

		// No other pocket can be used case.
		recipient = "Adam";
		selection = CreateLabelSelectionViewModel(Money.Parse("0.5"), recipient);
		await selection.ResetAsync(pockets.ToArray());
		output = await selection.AutoSelectPocketsAsync();
		Assert.False(selection.IsOtherSelectionPossible(output.SelectMany(x => x.Coins), recipient));

		// Exact match. Recipient == pocket, no better selection.
		recipient = "Dan";
		selection = CreateLabelSelectionViewModel(Money.Parse("0.5"), recipient);
		await selection.ResetAsync(pockets.ToArray());
		output = await selection.AutoSelectPocketsAsync();
		Assert.False(selection.IsOtherSelectionPossible(output.SelectMany(x => x.Coins), recipient));

		pockets.Add(privatePocket);
		await selection.ResetAsync(pockets.ToArray());

		// Private funds are enough for the payment, no better selection.
		recipient = "Doesn't matter, it will use private coins";
		selection = CreateLabelSelectionViewModel(Money.Parse("0.5"), recipient);
		await selection.ResetAsync(pockets.ToArray());
		output = await selection.AutoSelectPocketsAsync();
		Assert.False(selection.IsOtherSelectionPossible(output.SelectMany(x => x.Coins), recipient));

		pockets.Remove(privatePocket);
		pockets.Add(semiPrivatePocket);
		selection = CreateLabelSelectionViewModel(Money.Parse("0.5"), recipient);
		await selection.ResetAsync(pockets.ToArray());

		// Semi funds are enough for the payment, no better selection.
		output = await selection.AutoSelectPocketsAsync();
		Assert.False(selection.IsOtherSelectionPossible(output.SelectMany(x => x.Coins), recipient));

		pockets.Add(privatePocket);
		selection = CreateLabelSelectionViewModel(Money.Parse("3.5"), recipient);
		await selection.ResetAsync(pockets.ToArray());

		// Private and semi funds are enough for the payment, no better selection.
		output = await selection.AutoSelectPocketsAsync();
		Assert.False(selection.IsOtherSelectionPossible(output.SelectMany(x => x.Coins), recipient));
	}
}

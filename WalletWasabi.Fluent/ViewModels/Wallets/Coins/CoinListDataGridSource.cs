using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.TreeDataGrid;
using WalletWasabi.Fluent.ViewModels.CoinControl;
using WalletWasabi.Fluent.ViewModels.CoinControl.Core;
using WalletWasabi.Fluent.Views.CoinControl.Core.Cells;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Coins;

public static class CoinListDataGridSource
{
	public static HierarchicalTreeDataGridSource<CoinListItem> Create(IEnumerable<CoinListItem> source)
	{
		// [Column]			[View]					[Header]	[Width]		[MinWidth]		[MaxWidth]	[CanUserSort]
		// Indicators		IndicatorsColumnView	-			Auto		-				-			true
		// AnonymityScore	AnonymityColumnView		<custom>	50			-				-			true
		// Amount			AmountColumnView		Amount		Auto		-				-			true
		// Labels			LabelsColumnView		Labels		*			-				-			true
		// Selection		SelectionColumnView		-			Auto		-				-			false
		return new HierarchicalTreeDataGridSource<CoinListItem>(source)
		{
			Columns =
			{
				IndicatorsColumn(),
				AnonymityScoreColumn(),
				AmountColumn(),
				LabelsColumn(),
				SelectionColumn(),
			}
		};
	}

	private static int GetIndicatorPriority(CoinListItem x)
	{
		if (x.IsCoinjoining)
		{
			return 1;
		}

		if (x.BannedUntilUtc.HasValue)
		{
			return 2;
		}

		if (!x.IsConfirmed)
		{
			return 3;
		}

		return 0;
	}

	private static IColumn<CoinListItem> IndicatorsColumn()
	{
		return new HierarchicalExpanderColumn<CoinListItem>(
			new TemplateColumn<CoinListItem>(
				null,
				new FuncDataTemplate<CoinListItem>((_, _) => new IndicatorsCellView(), true),
				null,
				GridLength.Auto,
				new TemplateColumnOptions<CoinListItem>
				{
					CompareAscending = Sort<CoinListItem>.Ascending(GetIndicatorPriority),
					CompareDescending = Sort<CoinListItem>.Descending(GetIndicatorPriority)
				}),
			group => group.Children,
			node => node.HasChildren(),
			node => node.IsExpanded);
	}

	private static TemplateColumn<CoinListItem> SelectionColumn()
	{
		return new TemplateColumn<CoinListItem>(
			null,
			new FuncDataTemplate<CoinListItem>(
				(_, _) => new SelectionCellView(),
				true),
			null,
			GridLength.Auto);
	}

	private static IColumn<CoinListItem> AmountColumn()
	{
		return new PlainTextColumn<CoinListItem>(
			null,
			node => $"{node.Amount.ToFormattedString()} BTC",
			GridLength.Auto,
			new ColumnOptions<CoinListItem>
			{
				CompareAscending = Sort<CoinListItem>.Ascending(x => x.Amount),
				CompareDescending = Sort<CoinListItem>.Descending(x => x.Amount)
			});
	}

	private static IColumn<CoinListItem> AnonymityScoreColumn()
	{
		return new TemplateColumn<CoinListItem>(
			null,
			new FuncDataTemplate<CoinListItem>((_, _) => new AnonymityScoreCellView(), true),
			null,
			GridLength.Auto,
			new TemplateColumnOptions<CoinListItem>
			{
				CompareAscending = Sort<CoinListItem>.Ascending(b => b.AnonymityScore ?? b.Children.Min(x => x.AnonymityScore)),
				CompareDescending = Sort<CoinListItem>.Descending(b => b.AnonymityScore ?? b.Children.Min(x => x.AnonymityScore))
			});
	}

	private static IColumn<CoinListItem> LabelsColumn()
	{
		return new TemplateColumn<CoinListItem>(
			null,
			new FuncDataTemplate<CoinListItem>((_, _) => new LabelsCellView(), true),
			null,
			GridLength.Star,
			new TemplateColumnOptions<CoinListItem>
			{
				CompareAscending = CoinControlLabelComparer.Ascending,
				CompareDescending = CoinControlLabelComparer.Descending
			});
	}
}

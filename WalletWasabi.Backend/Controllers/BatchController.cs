using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.RPC;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend.Models;
using WalletWasabi.Backend.Models.Responses;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Models;

namespace WalletWasabi.Backend.Controllers;

/// <summary>
/// To make batched requests.
/// </summary>
[Produces("application/json")]
[Route("api/v" + Constants.BackendMajorVersion + "/btc/[controller]")]
public class BatchController : ControllerBase
{
	public BatchController(BlockchainController blockchainController, HomeController homeController, OffchainController offchainController, Global global)
	{
		BlockchainController = blockchainController;
		HomeController = homeController;
		OffchainController = offchainController;
		Global = global;
	}

	public Global Global { get; }
	public BlockchainController BlockchainController { get; }
	public HomeController HomeController { get; }
	public OffchainController OffchainController { get; }

	[HttpGet("synchronize")]
	public async Task<IActionResult> GetSynchronizeAsync(
		[FromQuery, Required] string bestKnownBlockHash,
		[FromQuery, Required] int maxNumberOfFilters,
		[FromQuery] string? estimateSmartFeeMode = nameof(EstimateSmartFeeMode.Conservative),
		[FromQuery] string? indexType = null,
		CancellationToken cancellationToken = default)
	{
		bool estimateSmartFee = !string.IsNullOrWhiteSpace(estimateSmartFeeMode);
		EstimateSmartFeeMode mode = EstimateSmartFeeMode.Conservative;
		if (estimateSmartFee)
		{
			if (!Enum.TryParse(estimateSmartFeeMode, ignoreCase: true, out mode))
			{
				return BadRequest("Invalid estimation mode is provided, possible values: ECONOMICAL/CONSERVATIVE.");
			}
		}

		if (!uint256.TryParse(bestKnownBlockHash, out var knownHash))
		{
			return BadRequest($"Invalid {nameof(bestKnownBlockHash)}.");
		}

		if (!BlockchainController.TryGetIndexer(indexType, out var indexer))
		{
			return BadRequest("Not supported index type.");
		}

		(Height bestHeight, IEnumerable<FilterModel> filters) = indexer.GetFilterLinesExcluding(knownHash, maxNumberOfFilters, out bool found);

		var response = new SynchronizeResponse { Filters = Enumerable.Empty<FilterModel>(), BestHeight = bestHeight };

		if (!found)
		{
			response.FiltersResponseState = FiltersResponseState.BestKnownHashNotFound;
		}
		else if (!filters.Any())
		{
			response.FiltersResponseState = FiltersResponseState.NoNewFilter;
		}
		else
		{
			response.FiltersResponseState = FiltersResponseState.NewFilters;
			response.Filters = filters;
		}

		if (estimateSmartFee)
		{
			try
			{
				response.AllFeeEstimate = await BlockchainController.GetAllFeeEstimateAsync(mode, cancellationToken);
			}
			catch (Exception ex)
			{
				Logger.LogError(ex);
			}
		}

		response.ExchangeRates = await OffchainController.GetExchangeRatesCollectionAsync(cancellationToken);

		return Ok(response);
	}
}

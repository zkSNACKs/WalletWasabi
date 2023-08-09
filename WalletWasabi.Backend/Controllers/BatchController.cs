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
	public BatchController(BlockchainController blockchainController, ChaumianCoinJoinController chaumianCoinJoinController, HomeController homeController, OffchainController offchainController, WabiSabiController wabiSabiController, Global global)
	{
		BlockchainController = blockchainController;
		ChaumianCoinJoinController = chaumianCoinJoinController;
		HomeController = homeController;
		OffchainController = offchainController;
		WabiSabiController = wabiSabiController;
		Global = global;
	}

	public Global Global { get; }
	public BlockchainController BlockchainController { get; }
	public ChaumianCoinJoinController ChaumianCoinJoinController { get; }
	public HomeController HomeController { get; }
	public OffchainController OffchainController { get; }
	public WabiSabiController WabiSabiController { get; }

	[HttpGet("synchronize")]
	[ResponseCache(Duration = 60)]
	public async Task<IActionResult> GetSynchronizeAsync(
		[FromQuery, Required] string bestKnownBlockHash,
		[FromQuery] string indexType = "segwittaproot",
		CancellationToken cancellationToken = default)
	{
		if (!uint256.TryParse(bestKnownBlockHash, out var knownHash))
		{
			return BadRequest($"Invalid {nameof(bestKnownBlockHash)}.");
		}

		if (!BlockchainController.TryGetIndexer(indexType, out var indexer))
		{
			return BadRequest("Not supported index type.");
		}

		var numberOfFilters = Global.Config.Network == Network.Main ? 1000 : 10000;
		(Height bestHeight, bool found, IEnumerable<FilterModel> filters) = await indexer.GetFilterLinesExcludingAsync(knownHash, numberOfFilters);

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

		response.CcjRoundStates = ChaumianCoinJoinController.GetStatesCollection();

		try
		{
			response.AllFeeEstimate = await BlockchainController.GetAllFeeEstimateAsync(EstimateSmartFeeMode.Conservative, cancellationToken);
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
		}

		response.ExchangeRates = await OffchainController.GetExchangeRatesCollectionAsync(cancellationToken);

		response.UnconfirmedCoinJoins = ChaumianCoinJoinController.GetUnconfirmedCoinJoinCollection().Concat(WabiSabiController.GetUnconfirmedCoinJoinCollection()).Distinct();

		return Ok(response);
	}
}

using Microsoft.AspNetCore.Mvc;
using WalletWasabi.Backend.Models.Responses;
using WalletWasabi.Helpers;

namespace WalletWasabi.Backend.Controllers
{
	/// <summary>
	/// To acquire administrative data about the software.
	/// </summary>
	[ApiController]
	[Produces("application/json")]
	[Route("api/[controller]")]
	public class SoftwareController : ControllerBase
	{
		private readonly VersionsResponse VersionsResponse = new VersionsResponse
		{
			ClientVersion = Constants.ClientVersion.ToString(3),
			BackendMajorVersion = Constants.BackendMajorVersion,
			LegalDocumentsVersion = Constants.LegalDocumentsVersion.ToString()
		};

		/// <summary>
		/// Gets the latest versions of the client and backend.
		/// </summary>
		/// <returns>ClientVersion, BackendMajorVersion.</returns>
		/// <response code="200">ClientVersion, BackendMajorVersion.</response>
		[HttpGet("versions")]
		[ProducesResponseType(typeof(VersionsResponse), 200)]
		public VersionsResponse GetVersions()
		{
			return VersionsResponse;
		}
	}
}

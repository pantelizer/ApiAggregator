using ApiAggregator.Models;
using ApiAggregator.Services.Aggregation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiAggregator.Controllers;

/// <summary>
/// The single unified endpoint that returns data aggregated from all external sources.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize] // Requires a valid JWT bearer token (see AuthController to obtain one).
public sealed class AggregationController : ControllerBase
{
    private readonly IAggregationService _aggregationService;

    public AggregationController(IAggregationService aggregationService)
    {
        _aggregationService = aggregationService;
    }

    /// <summary>
    /// Retrieve aggregated, filtered and sorted data from all configured external APIs.
    /// </summary>
    /// <remarks>
    /// Example: <c>GET /api/aggregation?city=Berlin&amp;keyword=ai&amp;sortBy=Date&amp;sortDir=Descending</c>
    /// </remarks>
    /// <param name="query">Search terms plus filter and sort parameters (bound from the query string).</param>
    /// <param name="cancellationToken">Cancellation token tied to the HTTP request.</param>
    [HttpGet]
    [ProducesResponseType(typeof(AggregatedResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AggregatedResponse>> Get(
        [FromQuery] AggregationQuery query, CancellationToken cancellationToken)
    {
        var result = await _aggregationService.AggregateAsync(query, cancellationToken);
        return Ok(result);
    }
}

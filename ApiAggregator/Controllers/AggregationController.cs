using ApiAggregator.Infrastructure.Swagger;
using ApiAggregator.Models;
using ApiAggregator.Services.Aggregation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiAggregator.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class AggregationController : ControllerBase
{
    private readonly IAggregationService _aggregationService;

    public AggregationController(IAggregationService aggregationService)
    {
        _aggregationService = aggregationService;
    }

    /// <summary>Get aggregated, filtered and sorted data from the external APIs.</summary>
    [HttpGet]
    [IncludeSourceParameters]
    [ProducesResponseType(typeof(AggregatedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AggregatedResponse>> Get(
        [FromQuery] AggregationQuery query, CancellationToken cancellationToken)
    {
        query.Parameters = Request.Query.ToDictionary(
            p => p.Key, p => p.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        var known = _aggregationService.GetSources().Select(s => s.Name).ToList();
        var unknown = query.GetRequestedSources()
            .Where(s => !known.Contains(s, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (unknown.Count > 0)
        {
            ModelState.AddModelError(nameof(AggregationQuery.Sources),
                $"Unknown source(s): {string.Join(", ", unknown)}. Valid sources: {string.Join(", ", known)}.");
            return ValidationProblem(ModelState);
        }

        var result = await _aggregationService.AggregateAsync(query, cancellationToken);
        return Ok(result);
    }

    /// <summary>List the available sources and their own query parameters.</summary>
    [HttpGet("sources")]
    [ProducesResponseType(typeof(IReadOnlyList<SourceInfo>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<SourceInfo>> GetSources() => Ok(_aggregationService.GetSources());
}

using ApiAggregator.Models;
using ApiAggregator.Services.Statistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiAggregator.Controllers;


[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class StatisticsController : ControllerBase
{
    private readonly IStatisticsService _statistics;

    public StatisticsController(IStatisticsService statistics)
    {
        _statistics = statistics;
    }

    /// <summary>Get statistics for every tracked external API.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ApiStatisticsSnapshot>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ApiStatisticsSnapshot>> GetAll()
    {
        return Ok(_statistics.GetSnapshots());
    }

    /// <summary>Get statistics for a single API by name (e.g. "Weather").</summary>
    [HttpGet("{apiName}")]
    [ProducesResponseType(typeof(ApiStatisticsSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ApiStatisticsSnapshot> GetByName(string apiName)
    {
        var snapshot = _statistics.GetSnapshot(apiName);
        return snapshot is null ? NotFound() : Ok(snapshot);
    }
}

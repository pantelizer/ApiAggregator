using ApiAggregator.Models;

namespace ApiAggregator.Services.Statistics;

/// <summary>
/// Thread-safe in-memory store of per-API request statistics.
/// Implementations must be safe to call concurrently from many request threads.
/// </summary>
public interface IStatisticsService
{
    /// <summary>
    /// Record a single completed request to an external API.
    /// </summary>
    /// <param name="apiName">Logical API name, e.g. "Weather".</param>
    /// <param name="elapsedMs">How long the request took, in milliseconds.</param>
    /// <param name="failed">True if the request failed (exception or non-success status).</param>
    void Record(string apiName, double elapsedMs, bool failed);

    /// <summary>Get a point-in-time snapshot for every tracked API.</summary>
    IReadOnlyList<ApiStatisticsSnapshot> GetSnapshots();

    /// <summary>Get a snapshot for one API, or null if it has no recorded requests.</summary>
    ApiStatisticsSnapshot? GetSnapshot(string apiName);

    /// <summary>
    /// Compare each API's recent rolling average against its lifetime average and return
    /// any that exceed the configured anomaly ratio threshold.
    /// </summary>
    IReadOnlyList<AnomalyReport> DetectAnomalies();
}

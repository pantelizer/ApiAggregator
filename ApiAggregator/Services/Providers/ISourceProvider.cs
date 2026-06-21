using ApiAggregator.Models;

namespace ApiAggregator.Services.Providers;

/// <summary>
/// One external data source. To integrate a new API you implement this interface and register
/// it in DI — the aggregation service automatically picks up every registered provider, fetches
/// them all in parallel, and merges their normalized output. No other code needs to change.
/// </summary>
public interface ISourceProvider
{
    /// <summary>Stable, human-readable name of the source, e.g. "Weather".</summary>
    string SourceName { get; }

    /// <summary>
    /// Fetch and normalize this source's data for the given query. May serve from cache.
    /// Implementations should throw on failure; the aggregation service isolates the failure
    /// so one bad source does not fail the whole response.
    /// </summary>
    Task<ProviderResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken);
}

/// <summary>The outcome of a single provider fetch, including whether it came from cache.</summary>
public sealed class ProviderResult
{
    public required IReadOnlyList<AggregatedItem> Items { get; init; }
    public bool FromCache { get; init; }
}

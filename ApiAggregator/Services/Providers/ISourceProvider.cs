using ApiAggregator.Models;

namespace ApiAggregator.Services.Providers;

public interface ISourceProvider
{
    string SourceName { get; }

    IReadOnlyList<SourceParameter> Parameters => [];

    // Throws on failure; AggregationService isolates it from the other sources.
    Task<ProviderResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken);
}

public sealed class ProviderResult
{
    public required IReadOnlyList<AggregatedItem> Items { get; init; }
    public bool FromCache { get; init; }
}

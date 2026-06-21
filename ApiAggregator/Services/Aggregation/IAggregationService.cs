using ApiAggregator.Models;

namespace ApiAggregator.Services.Aggregation;

/// <summary>Orchestrates fetching from all providers and assembling the unified response.</summary>
public interface IAggregationService
{
    Task<AggregatedResponse> AggregateAsync(AggregationQuery query, CancellationToken cancellationToken);
}

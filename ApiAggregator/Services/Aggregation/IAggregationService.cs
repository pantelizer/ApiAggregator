using ApiAggregator.Models;

namespace ApiAggregator.Services.Aggregation;

public interface IAggregationService
{
    Task<AggregatedResponse> AggregateAsync(AggregationQuery query, CancellationToken cancellationToken);

    IReadOnlyList<SourceInfo> GetSources();
}

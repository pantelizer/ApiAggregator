using ApiAggregator.Models;
using ZiggyCreatures.Caching.Fusion;

namespace ApiAggregator.Services.Providers;

public abstract class SourceProviderBase : ISourceProvider
{
    private readonly IFusionCache _cache;

    protected SourceProviderBase(IFusionCache cache)
    {
        _cache = cache;
    }

    public abstract string SourceName { get; }

    public virtual IReadOnlyList<SourceParameter> Parameters => [];

    protected abstract TimeSpan CacheTtl { get; }

    protected abstract string BuildCacheKey(AggregationQuery query);

    protected abstract Task<IReadOnlyList<AggregatedItem>> FetchFromSourceAsync(
        AggregationQuery query, CancellationToken cancellationToken);

    public async Task<ProviderResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        var key = $"{SourceName}:{BuildCacheKey(query)}";

        IReadOnlyList<AggregatedItem>? fetchedByThisCall = null;

        var items = await _cache.GetOrSetAsync<IReadOnlyList<AggregatedItem>>(
            key,
            async (_, ct) =>
            {
                var fetched = await FetchFromSourceAsync(query, ct);
                fetchedByThisCall = fetched;
                return fetched;
            },
            options => options.SetDuration(CacheTtl),
            cancellationToken);

        return new ProviderResult
        {
            Items = items,
            FromCache = !ReferenceEquals(items, fetchedByThisCall)
        };
    }
}

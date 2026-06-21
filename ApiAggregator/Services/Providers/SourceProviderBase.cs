using ApiAggregator.Models;
using Microsoft.Extensions.Caching.Memory;

namespace ApiAggregator.Services.Providers;

public abstract class SourceProviderBase : ISourceProvider
{
    private readonly IMemoryCache _cache;

    protected SourceProviderBase(IMemoryCache cache)
    {
        _cache = cache;
    }

    public abstract string SourceName { get; }

    protected abstract TimeSpan CacheTtl { get; }

    protected virtual TimeSpan StaleFallbackTtl => TimeSpan.FromHours(1);

    protected abstract string BuildCacheKey(AggregationQuery query);

    protected abstract Task<IReadOnlyList<AggregatedItem>> FetchFromSourceAsync(
        AggregationQuery query, CancellationToken cancellationToken);

    public async Task<ProviderResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        var freshKey = $"{SourceName}:fresh:{BuildCacheKey(query)}";
        var staleKey = $"{SourceName}:stale:{BuildCacheKey(query)}";

        if (_cache.TryGetValue(freshKey, out IReadOnlyList<AggregatedItem>? cached) && cached is not null)
        {
            return new ProviderResult { Items = cached, FromCache = true };
        }

        try
        {
            var items = await FetchFromSourceAsync(query, cancellationToken);

            _cache.Set(freshKey, items, CacheTtl);
            _cache.Set(staleKey, items, StaleFallbackTtl);

            return new ProviderResult { Items = items, FromCache = false };
        }
        catch when (_cache.TryGetValue(staleKey, out IReadOnlyList<AggregatedItem>? stale) && stale is not null)
        {
            return new ProviderResult { Items = stale, FromCache = true };
        }
    }
}

using ApiAggregator.Models;
using ApiAggregator.Services.Providers;
using Microsoft.Extensions.Caching.Memory;

namespace ApiAggregator.Tests;

public class SourceProviderBaseTests
{
    /// <summary>Test provider whose fetch behaviour and TTL are controllable.</summary>
    private sealed class TestProvider : SourceProviderBase
    {
        public int FetchCount { get; private set; }
        public bool ShouldThrow { get; set; }
        public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(5);

        public TestProvider(IMemoryCache cache) : base(cache) { }

        public override string SourceName => "Test";
        protected override TimeSpan CacheTtl => Ttl;
        protected override string BuildCacheKey(AggregationQuery query) => query.Keyword ?? "default";

        protected override Task<IReadOnlyList<AggregatedItem>> FetchFromSourceAsync(
            AggregationQuery query, CancellationToken cancellationToken)
        {
            FetchCount++;
            if (ShouldThrow)
            {
                throw new HttpRequestException("source unavailable");
            }

            IReadOnlyList<AggregatedItem> items = new[]
            {
                new AggregatedItem { Source = "Test", Category = "test", Title = $"item-{FetchCount}", Relevance = 1 }
            };
            return Task.FromResult(items);
        }
    }

    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

    [Fact]
    public async Task Second_call_within_ttl_is_served_from_cache_without_refetching()
    {
        var provider = new TestProvider(NewCache());
        var query = new AggregationQuery { Keyword = "k" };

        var first = await provider.FetchAsync(query, CancellationToken.None);
        var second = await provider.FetchAsync(query, CancellationToken.None);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(1, provider.FetchCount); // the external source was hit only once
    }

    [Fact]
    public async Task Failure_falls_back_to_last_known_good_stale_cache()
    {
        var provider = new TestProvider(NewCache())
        {
            // Tiny fresh TTL so the fresh entry expires, forcing a re-fetch on the second call.
            Ttl = TimeSpan.FromMilliseconds(1)
        };
        var query = new AggregationQuery { Keyword = "k" };

        // 1. Successful call populates the stale fallback copy.
        var first = await provider.FetchAsync(query, CancellationToken.None);
        Assert.False(first.FromCache);

        // Let the short-lived fresh entry expire.
        await Task.Delay(50);

        // 2. The source is now down: the fresh entry is gone, so it re-fetches, which throws,
        //    and the base class serves the stale fallback instead of failing.
        provider.ShouldThrow = true;
        var second = await provider.FetchAsync(query, CancellationToken.None);

        Assert.True(second.FromCache);
        Assert.Single(second.Items);
        Assert.Equal("item-1", second.Items[0].Title); // the previously cached good value
    }

    [Fact]
    public async Task Failure_with_no_cached_value_propagates_the_exception()
    {
        var provider = new TestProvider(NewCache()) { ShouldThrow = true };

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.FetchAsync(new AggregationQuery { Keyword = "k" }, CancellationToken.None));
    }
}

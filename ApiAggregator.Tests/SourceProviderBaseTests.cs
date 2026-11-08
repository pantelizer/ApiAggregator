using System.Diagnostics;
using ApiAggregator.Models;
using ApiAggregator.Services.Providers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace ApiAggregator.Tests;

public class SourceProviderBaseTests
{
    private sealed class TestProvider : SourceProviderBase
    {
        private int _fetchCount;

        public int FetchCount => Volatile.Read(ref _fetchCount);
        public bool ShouldThrow { get; set; }
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;
        public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(5);

        public TestProvider(IFusionCache cache) : base(cache) { }

        public override string SourceName => "Test";
        protected override TimeSpan CacheTtl => Ttl;
        protected override string BuildCacheKey(AggregationQuery query) => query.Keyword ?? "default";

        protected override async Task<IReadOnlyList<AggregatedItem>> FetchFromSourceAsync(
            AggregationQuery query, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _fetchCount);
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (ShouldThrow)
            {
                throw new HttpRequestException("source unavailable");
            }

            return new[]
            {
                new AggregatedItem { Source = "Test", Category = "test", Title = $"item-{n}", Relevance = 1 }
            };
        }
    }

    private static IFusionCache NewCache(TimeSpan? softTimeout = null) =>
        new FusionCache(new FusionCacheOptions
        {
            DefaultEntryOptions = new FusionCacheEntryOptions
            {
                IsFailSafeEnabled = true,
                FailSafeMaxDuration = TimeSpan.FromHours(1),
                FailSafeThrottleDuration = TimeSpan.FromSeconds(30),
                FactorySoftTimeout = softTimeout ?? Timeout.InfiniteTimeSpan
            }
        });

    private static readonly AggregationQuery Query = new() { Keyword = "k" };

    [Fact]
    public async Task Second_call_within_ttl_is_served_from_cache_without_refetching()
    {
        var provider = new TestProvider(NewCache());

        var first = await provider.FetchAsync(Query, CancellationToken.None);
        var second = await provider.FetchAsync(Query, CancellationToken.None);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(1, provider.FetchCount);
    }

    [Fact]
    public async Task Failure_falls_back_to_last_known_good_value()
    {
        var provider = new TestProvider(NewCache()) { Ttl = TimeSpan.FromMilliseconds(1) };

        var first = await provider.FetchAsync(Query, CancellationToken.None);
        Assert.False(first.FromCache);

        await Task.Delay(50);

        provider.ShouldThrow = true;
        var second = await provider.FetchAsync(Query, CancellationToken.None);

        Assert.True(second.FromCache);
        Assert.Single(second.Items);
        Assert.Equal("item-1", second.Items[0].Title);
        Assert.Equal(2, provider.FetchCount);
    }

    [Fact]
    public async Task Failure_with_no_cached_value_propagates_the_exception()
    {
        var provider = new TestProvider(NewCache()) { ShouldThrow = true };

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.FetchAsync(Query, CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_identical_requests_trigger_a_single_fetch()
    {
        var provider = new TestProvider(NewCache()) { Delay = TimeSpan.FromMilliseconds(200) };

        var results = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => provider.FetchAsync(Query, CancellationToken.None)));

        Assert.Equal(1, provider.FetchCount);
        Assert.Single(results, r => !r.FromCache);
        Assert.All(results, r => Assert.Equal("item-1", r.Items[0].Title));
    }

    [Fact]
    public async Task Second_instance_is_served_from_the_shared_distributed_cache()
    {
        var sharedL2 = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        IFusionCache NewHybridCache() =>
            new FusionCache(new FusionCacheOptions())
                .SetupSerializer(new FusionCacheSystemTextJsonSerializer())
                .SetupDistributedCache(sharedL2);

        var instanceA = new TestProvider(NewHybridCache());
        var instanceB = new TestProvider(NewHybridCache());

        var fromA = await instanceA.FetchAsync(Query, CancellationToken.None);
        var fromB = await instanceB.FetchAsync(Query, CancellationToken.None);

        Assert.False(fromA.FromCache);
        Assert.True(fromB.FromCache);
        Assert.Equal(0, instanceB.FetchCount);
        Assert.Equal("item-1", fromB.Items[0].Title);
    }

    [Fact]
    public async Task Slow_source_with_a_stale_value_returns_the_stale_value_within_the_soft_timeout()
    {
        var provider = new TestProvider(NewCache(softTimeout: TimeSpan.FromMilliseconds(100)))
        {
            Ttl = TimeSpan.FromMilliseconds(1)
        };

        await provider.FetchAsync(Query, CancellationToken.None);
        await Task.Delay(50);

        provider.Delay = TimeSpan.FromSeconds(2);
        var stopwatch = Stopwatch.StartNew();
        var result = await provider.FetchAsync(Query, CancellationToken.None);
        stopwatch.Stop();

        Assert.True(result.FromCache);
        Assert.Equal("item-1", result.Items[0].Title);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"took {stopwatch.Elapsed}");
    }
}

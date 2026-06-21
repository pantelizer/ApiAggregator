using ApiAggregator.Models;
using ApiAggregator.Services.Aggregation;
using ApiAggregator.Services.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiAggregator.Tests;

public class AggregationServiceTests
{
    /// <summary>A configurable fake provider for driving the aggregation service.</summary>
    private sealed class FakeProvider : ISourceProvider
    {
        private readonly IReadOnlyList<AggregatedItem> _items;
        private readonly Exception? _throw;

        public FakeProvider(string name, IReadOnlyList<AggregatedItem> items, Exception? toThrow = null)
        {
            SourceName = name;
            _items = items;
            _throw = toThrow;
        }

        public string SourceName { get; }

        public Task<ProviderResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
        {
            if (_throw is not null)
            {
                throw _throw;
            }
            return Task.FromResult(new ProviderResult { Items = _items });
        }
    }

    private static AggregatedItem Item(string source, string category, double relevance, DateTimeOffset? date = null, string? title = null) =>
        new()
        {
            Source = source,
            Category = category,
            Title = title ?? $"{source}-{relevance}",
            Relevance = relevance,
            Date = date
        };

    private static AggregationService Build(params ISourceProvider[] providers) =>
        new(providers, NullLogger<AggregationService>.Instance);

    [Fact]
    public async Task Aggregate_merges_items_from_all_providers()
    {
        var service = Build(
            new FakeProvider("A", new[] { Item("A", "x", 1) }),
            new FakeProvider("B", new[] { Item("B", "y", 2) }));

        var result = await service.AggregateAsync(new AggregationQuery(), CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, i => i.Source == "A");
        Assert.Contains(result.Items, i => i.Source == "B");
        Assert.All(result.Sources, s => Assert.True(s.Succeeded));
    }

    [Fact]
    public async Task Aggregate_isolates_a_failing_provider_and_still_returns_others()
    {
        var service = Build(
            new FakeProvider("Good", new[] { Item("Good", "x", 1) }),
            new FakeProvider("Bad", Array.Empty<AggregatedItem>(), new InvalidOperationException("API down")));

        var result = await service.AggregateAsync(new AggregationQuery(), CancellationToken.None);

        // The good source's data is still returned.
        Assert.Single(result.Items);
        Assert.Equal("Good", result.Items[0].Source);

        // The failing source is reported as failed with its error message (the fallback contract).
        var bad = result.Sources.Single(s => s.Source == "Bad");
        Assert.False(bad.Succeeded);
        Assert.Equal("API down", bad.Error);

        var good = result.Sources.Single(s => s.Source == "Good");
        Assert.True(good.Succeeded);
    }

    [Fact]
    public async Task Aggregate_filters_by_source_and_category()
    {
        var service = Build(
            new FakeProvider("News", new[] { Item("News", "news", 1), Item("News", "news", 2) }),
            new FakeProvider("GitHub", new[] { Item("GitHub", "repository", 3) }));

        var query = new AggregationQuery { Sources = new[] { "News" }, Category = "news" };
        var result = await service.AggregateAsync(query, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, i => Assert.Equal("News", i.Source));
    }

    [Fact]
    public async Task Aggregate_filters_by_date_range()
    {
        var jan = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var jun = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var dec = new DateTimeOffset(2024, 12, 1, 0, 0, 0, TimeSpan.Zero);

        var service = Build(new FakeProvider("A", new[]
        {
            Item("A", "x", 1, jan),
            Item("A", "x", 2, jun),
            Item("A", "x", 3, dec)
        }));

        var query = new AggregationQuery
        {
            FromDate = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero),
            ToDate = new DateTimeOffset(2024, 9, 1, 0, 0, 0, TimeSpan.Zero)
        };
        var result = await service.AggregateAsync(query, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(jun, result.Items[0].Date);
    }

    [Fact]
    public async Task Aggregate_sorts_by_relevance_descending_by_default()
    {
        var service = Build(new FakeProvider("A", new[]
        {
            Item("A", "x", 1),
            Item("A", "x", 5),
            Item("A", "x", 3)
        }));

        var result = await service.AggregateAsync(new AggregationQuery(), CancellationToken.None);

        Assert.Equal(new double[] { 5, 3, 1 }, result.Items.Select(i => i.Relevance));
    }

    [Fact]
    public async Task Aggregate_sorts_by_date_ascending_when_requested()
    {
        var early = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var late = new DateTimeOffset(2024, 12, 1, 0, 0, 0, TimeSpan.Zero);

        var service = Build(new FakeProvider("A", new[]
        {
            Item("A", "x", 1, late, "late"),
            Item("A", "x", 2, early, "early")
        }));

        var query = new AggregationQuery { SortBy = SortField.Date, SortDir = SortDirection.Ascending };
        var result = await service.AggregateAsync(query, CancellationToken.None);

        Assert.Equal(new[] { "early", "late" }, result.Items.Select(i => i.Title));
    }
}

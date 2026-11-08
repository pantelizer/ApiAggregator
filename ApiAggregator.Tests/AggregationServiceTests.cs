using ApiAggregator.Models;
using ApiAggregator.Services.Aggregation;
using ApiAggregator.Services.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiAggregator.Tests;

public class AggregationServiceTests
{
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

        public int CallCount { get; private set; }

        public Task<ProviderResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
        {
            CallCount++;
            if (_throw is not null)
            {
                throw _throw;
            }
            return Task.FromResult(new ProviderResult { Items = _items });
        }
    }

    private sealed class ConcurrencyTracker
    {
        private int _current;
        private int _max;

        public int MaxConcurrent => Volatile.Read(ref _max);

        public void Enter()
        {
            var now = Interlocked.Increment(ref _current);
            int seen;
            while (now > (seen = Volatile.Read(ref _max)) &&
                   Interlocked.CompareExchange(ref _max, now, seen) != seen)
            {
            }
        }

        public void Exit() => Interlocked.Decrement(ref _current);
    }

    private sealed class SlowProvider : ISourceProvider
    {
        private readonly TimeSpan _delay;
        private readonly ConcurrencyTracker _tracker;

        public SlowProvider(string name, TimeSpan delay, ConcurrencyTracker tracker)
        {
            SourceName = name;
            _delay = delay;
            _tracker = tracker;
        }

        public string SourceName { get; }

        public async Task<ProviderResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
        {
            _tracker.Enter();
            try
            {
                await Task.Delay(_delay, cancellationToken);
                return new ProviderResult { Items = new[] { Item(SourceName, "x", 1) } };
            }
            finally
            {
                _tracker.Exit();
            }
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
    public async Task Aggregate_calls_providers_in_parallel()
    {
        var delay = TimeSpan.FromMilliseconds(500);
        var tracker = new ConcurrencyTracker();
        var service = Build(
            new SlowProvider("A", delay, tracker),
            new SlowProvider("B", delay, tracker),
            new SlowProvider("C", delay, tracker));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.AggregateAsync(new AggregationQuery(), CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal(3, result.TotalCount);

        Assert.Equal(3, tracker.MaxConcurrent);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(1000),
            $"Expected parallel execution (~500ms) but took {stopwatch.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public async Task Aggregate_isolates_a_failing_provider_and_still_returns_others()
    {
        var service = Build(
            new FakeProvider("Good", new[] { Item("Good", "x", 1) }),
            new FakeProvider("Bad", Array.Empty<AggregatedItem>(), new InvalidOperationException("API down")));

        var result = await service.AggregateAsync(new AggregationQuery(), CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Good", result.Items[0].Source);

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
    public async Task Aggregate_only_calls_the_requested_sources()
    {
        var weather = new FakeProvider("Weather", new[] { Item("Weather", "weather", 0) });
        var news = new FakeProvider("News", new[] { Item("News", "news", 1) });
        var gitHub = new FakeProvider("GitHub", new[] { Item("GitHub", "repository", 2) });
        var service = Build(weather, news, gitHub);

        var result = await service.AggregateAsync(
            new AggregationQuery { Sources = new[] { "GitHub", "weather" } }, CancellationToken.None);

        Assert.Equal(0, news.CallCount);
        Assert.Equal(1, weather.CallCount);
        Assert.Equal(1, gitHub.CallCount);
        Assert.Equal(new[] { "Weather", "GitHub" }, result.Sources.Select(s => s.Source));
        Assert.Equal(2, result.TotalCount);
    }

    [Theory]
    [InlineData(new string?[] { null }, 3)]
    [InlineData(new[] { "" }, 3)]
    [InlineData(new[] { " ", "" }, 3)]
    [InlineData(new[] { "News,GitHub" }, 3)]
    [InlineData(new[] { "", "github" }, 1)]
    public async Task Aggregate_ignores_blank_source_filters_and_accepts_comma_separated(string[] sources, int expected)
    {
        var service = Build(
            new FakeProvider("News", new[] { Item("News", "news", 1), Item("News", "news", 2) }),
            new FakeProvider("GitHub", new[] { Item("GitHub", "repository", 3) }));

        var result = await service.AggregateAsync(new AggregationQuery { Sources = sources }, CancellationToken.None);

        Assert.Equal(expected, result.TotalCount);
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

    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    [InlineData("sv-SE")]
    public async Task Title_sort_is_the_same_in_every_culture(string culture)
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            var service = Build(new FakeProvider("A", new[]
            {
                Item("A", "x", 1, title: "beta"),
                Item("A", "x", 2, title: "Alpha"),
                Item("A", "x", 3, title: "item"),
                Item("A", "x", 4, title: "Zulu"),
                Item("A", "x", 5, title: "aaa")
            }));

            var result = await service.AggregateAsync(
                new AggregationQuery { SortBy = SortField.Title, SortDir = SortDirection.Ascending }, CancellationToken.None);

            Assert.Equal(new[] { "aaa", "Alpha", "beta", "item", "Zulu" }, result.Items.Select(i => i.Title));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    private static AggregationQuery DateQuery(string? from, string? to)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (from is not null) parameters["FromDate"] = from;
        if (to is not null) parameters["ToDate"] = to;
        return new AggregationQuery
        {
            FromDate = from is null ? null : DateTimeOffset.Parse(from, System.Globalization.CultureInfo.InvariantCulture),
            ToDate = to is null ? null : DateTimeOffset.Parse(to, System.Globalization.CultureInfo.InvariantCulture),
            Parameters = parameters
        };
    }

    [Fact]
    public async Task Date_only_ToDate_includes_the_whole_day()
    {
        var service = Build(new FakeProvider("A", new[]
        {
            Item("A", "x", 1, DateTimeOffset.Parse("2026-11-07T00:00:00Z"), "start of 7th"),
            Item("A", "x", 2, DateTimeOffset.Parse("2026-11-07T18:30:00Z"), "evening of 7th"),
            Item("A", "x", 3, DateTimeOffset.Parse("2026-11-08T00:00:00Z"), "8th")
        }));

        var result = await service.AggregateAsync(DateQuery(from: null, to: "2026-11-07"), CancellationToken.None);

        Assert.Equal(new[] { "evening of 7th", "start of 7th" }, result.Items.Select(i => i.Title).Order());
    }

    [Fact]
    public async Task Date_only_FromDate_starts_at_midnight_utc()
    {
        var service = Build(new FakeProvider("A", new[]
        {
            Item("A", "x", 1, DateTimeOffset.Parse("2026-11-06T23:59:00Z"), "6th"),
            Item("A", "x", 2, DateTimeOffset.Parse("2026-11-07T00:00:00Z"), "7th")
        }));

        var result = await service.AggregateAsync(DateQuery(from: "2026-11-07", to: null), CancellationToken.None);

        Assert.Equal("7th", Assert.Single(result.Items).Title);
    }

    [Fact]
    public async Task ToDate_with_a_time_is_used_exactly()
    {
        var service = Build(new FakeProvider("A", new[]
        {
            Item("A", "x", 1, DateTimeOffset.Parse("2026-11-07T09:00:00Z"), "morning"),
            Item("A", "x", 2, DateTimeOffset.Parse("2026-11-07T18:30:00Z"), "evening")
        }));

        var result = await service.AggregateAsync(DateQuery(from: null, to: "2026-11-07T12:00:00Z"), CancellationToken.None);

        Assert.Equal("morning", Assert.Single(result.Items).Title);
    }

    private sealed class WaitingProvider : ISourceProvider
    {
        public string SourceName => "Slow";

        public async Task<ProviderResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    [Fact]
    public async Task Client_cancellation_propagates_instead_of_being_reported_as_a_failure()
    {
        var service = Build(new WaitingProvider(), new FakeProvider("A", new[] { Item("A", "x", 1) }));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.AggregateAsync(new AggregationQuery(), cts.Token));
    }

    [Fact]
    public async Task Provider_timeout_is_still_reported_as_a_failed_source()
    {
        var service = Build(
            new FakeProvider("Timeout", Array.Empty<AggregatedItem>(), new TaskCanceledException("The request timed out.")),
            new FakeProvider("A", new[] { Item("A", "x", 1) }));

        var result = await service.AggregateAsync(new AggregationQuery(), CancellationToken.None);

        Assert.Single(result.Items);
        var timedOut = result.Sources.Single(s => s.Source == "Timeout");
        Assert.False(timedOut.Succeeded);
        Assert.Equal("The request timed out.", timedOut.Error);
    }

    [Fact]
    public void GetSources_lists_every_provider_with_its_parameters()
    {
        var service = Build(new FakeProvider("A", Array.Empty<AggregatedItem>()), new WaitingProvider());

        var sources = service.GetSources();

        Assert.Equal(new[] { "A", "Slow" }, sources.Select(s => s.Name));
        Assert.All(sources, s => Assert.Empty(s.Parameters));
    }
}

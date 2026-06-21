using ApiAggregator.Models;
using ApiAggregator.Services.Providers;

namespace ApiAggregator.Services.Aggregation;

public sealed class AggregationService : IAggregationService
{
    private readonly IEnumerable<ISourceProvider> _providers;
    private readonly ILogger<AggregationService> _logger;

    public AggregationService(IEnumerable<ISourceProvider> providers, ILogger<AggregationService> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    public async Task<AggregatedResponse> AggregateAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        var tasks = _providers.Select(provider => FetchSafelyAsync(provider, query, cancellationToken));
        var results = await Task.WhenAll(tasks);

        var merged = results.SelectMany(r => r.Items);
        var filtered = ApplyFilters(merged, query);
        var sorted = ApplySort(filtered, query).ToList();

        var sources = results
            .Select(r => new SourceStatus
            {
                Source = r.SourceName,
                Succeeded = r.Succeeded,
                ItemCount = r.Items.Count,
                FromCache = r.FromCache,
                Error = r.Error
            })
            .ToList();

        return new AggregatedResponse
        {
            Items = sorted,
            TotalCount = sorted.Count,
            Sources = sources
        };
    }

    /// <summary>Invoke one provider, converting any failure into a recorded, non-fatal result.</summary>
    private async Task<ProviderFetchOutcome> FetchSafelyAsync(
        ISourceProvider provider, AggregationQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var result = await provider.FetchAsync(query, cancellationToken);
            return new ProviderFetchOutcome
            {
                SourceName = provider.SourceName,
                Items = result.Items,
                FromCache = result.FromCache,
                Succeeded = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider {Source} failed to return data.", provider.SourceName);
            return new ProviderFetchOutcome
            {
                SourceName = provider.SourceName,
                Items = Array.Empty<AggregatedItem>(),
                Succeeded = false,
                Error = ex.Message
            };
        }
    }

    private static IEnumerable<AggregatedItem> ApplyFilters(IEnumerable<AggregatedItem> items, AggregationQuery query)
    {
        if (query.Sources is { Length: > 0 })
        {
            var allowed = new HashSet<string>(query.Sources, StringComparer.OrdinalIgnoreCase);
            items = items.Where(i => allowed.Contains(i.Source));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            items = items.Where(i => string.Equals(i.Category, query.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (query.FromDate is { } from)
        {
            items = items.Where(i => i.Date is null || i.Date >= from);
        }

        if (query.ToDate is { } to)
        {
            items = items.Where(i => i.Date is null || i.Date <= to);
        }

        return items;
    }

    private static IEnumerable<AggregatedItem> ApplySort(IEnumerable<AggregatedItem> items, AggregationQuery query)
    {
        var descending = query.SortDir == SortDirection.Descending;

        Func<AggregatedItem, object?> keySelector = query.SortBy switch
        {
            SortField.Date => i => i.Date,
            SortField.Source => i => i.Source,
            SortField.Title => i => i.Title,
            _ => i => i.Relevance
        };

        return descending
            ? items.OrderByDescending(keySelector)
            : items.OrderBy(keySelector);
    }

    /// <summary>Internal carrier joining a provider's identity with its fetch outcome.</summary>
    private sealed class ProviderFetchOutcome
    {
        public required string SourceName { get; init; }
        public required IReadOnlyList<AggregatedItem> Items { get; init; }
        public bool FromCache { get; init; }
        public bool Succeeded { get; init; }
        public string? Error { get; init; }
    }
}

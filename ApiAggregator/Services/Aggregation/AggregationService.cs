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
        var tasks = SelectProviders(query)
            .Select(provider => FetchSafelyAsync(provider, query, cancellationToken))
            .ToArray();
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

    public IReadOnlyList<SourceInfo> GetSources() =>
        _providers.Select(p => new SourceInfo { Name = p.SourceName, Parameters = p.Parameters }).ToList();

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away and the request was cancelled.
            throw;
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

    private IEnumerable<ISourceProvider> SelectProviders(AggregationQuery query)
    {
        var requestedSources = query.GetRequestedSources();

        return requestedSources.Count == 0
            ? _providers
            : _providers.Where(p => requestedSources.Contains(p.SourceName));
    }

    private static IEnumerable<AggregatedItem> ApplyFilters(IEnumerable<AggregatedItem> items, AggregationQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            items = items.Where(i => string.Equals(i.Category, query.Category, StringComparison.OrdinalIgnoreCase));
        }

        var (from, to) = query.GetDateRange();

        if (from is { } fromValue)
        {
            items = items.Where(i => i.Date is null || i.Date >= fromValue);
        }

        if (to is { } toValue)
        {
            items = items.Where(i => i.Date is null || i.Date <= toValue);
        }

        return items;
    }

    private static IEnumerable<AggregatedItem> ApplySort(IEnumerable<AggregatedItem> items, AggregationQuery query)
    {
        var descending = query.SortDir == SortDirection.Descending;

        // Ordinal string comparison, so the order doesn't depend on the server's culture.
        return query.SortBy switch
        {
            SortField.Date => Order(items, i => i.Date, Comparer<DateTimeOffset?>.Default, descending),
            SortField.Source => Order(items, i => i.Source, StringComparer.OrdinalIgnoreCase, descending),
            SortField.Title => Order(items, i => i.Title, StringComparer.OrdinalIgnoreCase, descending),
            _ => Order(items, i => i.Relevance, Comparer<double>.Default, descending)
        };
    }

    private static IEnumerable<AggregatedItem> Order<TKey>(
        IEnumerable<AggregatedItem> items, Func<AggregatedItem, TKey> key, IComparer<TKey> comparer, bool descending) =>
        descending ? items.OrderByDescending(key, comparer) : items.OrderBy(key, comparer);

    private sealed class ProviderFetchOutcome
    {
        public required string SourceName { get; init; }
        public required IReadOnlyList<AggregatedItem> Items { get; init; }
        public bool FromCache { get; init; }
        public bool Succeeded { get; init; }
        public string? Error { get; init; }
    }
}

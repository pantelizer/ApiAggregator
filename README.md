# API Aggregator

A .NET 10 / ASP.NET Core service that fetches data from several external APIs **in parallel**,
normalizes it into a single shape, and exposes it through one unified, filterable/sortable
endpoint. It also tracks per-API request statistics, caches results, handles transient failures
with retries + a circuit breaker, falls back gracefully when a source is down, secures everything
with JWT, and logs performance anomalies from a background service.

Built for the "API Aggregation" assignment.

---

## Table of contents

- [Features mapped to requirements](#features-mapped-to-requirements)
- [Architecture](#architecture)
- [The external APIs](#the-external-apis)
- [Configuration (Options architecture)](#configuration-options-architecture)
- [Running the service](#running-the-service)
- [Authentication](#authentication)
- [API reference](#api-reference)
- [Caching, resilience & fallback](#caching-resilience--fallback)
- [Statistics & anomaly detection](#statistics--anomaly-detection)
- [Adding a new API](#adding-a-new-api)
- [Tests](#tests)

---

## Features mapped to requirements

| Requirement | Where it lives |
|---|---|
| ASP.NET Core aggregation service | whole `ApiAggregator` project |
| Architecture for easy integration of new APIs | `ISourceProvider` + `SourceProviderBase` |
| ≥3 external APIs fetched simultaneously | `WeatherSourceProvider`, `NewsSourceProvider`, `GitHubSourceProvider`, fanned out with `Task.WhenAll` in `AggregationService` |
| Unified endpoint | `GET /api/aggregation` |
| Filter & sort | `AggregationQuery` (source, category, date range, sort field/direction) |
| Error handling + transient failures + fallback | `Microsoft.Extensions.Http.Resilience` standard handler + per-provider isolation + stale-cache fallback |
| Unit tests | `ApiAggregator.Tests` |
| Documentation | this file + Swagger UI |
| Caching | `IMemoryCache` in `SourceProviderBase` |
| Parallelism | `Task.WhenAll` in `AggregationService` |
| Request statistics (in-memory, thread-safe) | `StatisticsService` + `StatisticsTrackingHandler`, `GET /api/statistics` |
| **(Optional)** JWT bearer auth | `JwtTokenService`, `AuthController`, JWT setup in `Program.cs` |
| **(Optional)** Anomaly background service | `PerformanceAnomalyMonitor` |

---

## Architecture

```
HTTP request
   │
   ▼
AggregationController ──► IAggregationService (AggregationService)
                               │  Task.WhenAll over all providers (parallel)
                               ▼
                 ┌─────────────┼─────────────┐
                 ▼             ▼             ▼
        WeatherProvider   NewsProvider   GitHubProvider     (each : SourceProviderBase)
                 │             │             │
        (IMemoryCache: fresh + stale-fallback per provider)
                 │             │             │
            typed HttpClient (per provider)
                 │  AddStandardResilienceHandler  (retry / timeout / circuit breaker)
                 │  StatisticsTrackingHandler      (times each call → IStatisticsService)
                 ▼
            external API
```

Key ideas:

- **Provider abstraction.** Every external API is an `ISourceProvider`. The aggregation service
  depends on `IEnumerable<ISourceProvider>`, so it automatically uses every provider registered in
  DI. Adding an API does not touch the aggregator.
- **Normalization.** Each provider maps its API's response into a common `AggregatedItem`
  (`Source`, `Category`, `Title`, `Description`, `Url`, `Date`, `Relevance`, `Extra`). Filtering
  and sorting then work uniformly across all sources.
- **Cross-cutting concerns by composition.** Caching + fallback live in `SourceProviderBase`;
  resilience and timing live in the HTTP pipeline (handlers). Providers stay small and focused on
  *fetch + map*.

---

## The external APIs

| Source | API | Needs a key? | Produces |
|---|---|---|---|
| `Weather` | [OpenWeatherMap](https://openweathermap.org/api) current weather | Yes | one item: current conditions for a city |
| `News` | [NewsAPI](https://newsapi.org/) `/everything` | Yes | one item per article |
| `GitHub` | [GitHub REST](https://docs.github.com/en/rest) repository search | Optional (higher rate limit with a token) | one item per repository |

> Without keys, the Weather and News providers will receive `401` from their APIs. That's fine for
> a demo: the service degrades gracefully and still returns GitHub results (which work
> unauthenticated). Supply real keys to see all three.

---

## Configuration (Options architecture)

Per the brief, **all** API keys, URLs, and other settings are bound from configuration into
strongly-typed Options classes — nothing sensitive is hard-coded in C#.

| Options class | Section | Notable settings |
|---|---|---|
| `WeatherApiOptions` | `ExternalApis:Weather` | `BaseUrl`, `ApiKey`, `Units`, `DefaultCity`, `CacheTtlSeconds`, `TimeoutSeconds` |
| `NewsApiOptions` | `ExternalApis:News` | `BaseUrl`, `ApiKey`, `DefaultQuery`, `PageSize`, `CacheTtlSeconds` |
| `GitHubApiOptions` | `ExternalApis:GitHub` | `BaseUrl`, `Token`, `UserAgent`, `DefaultQuery`, `PageSize`, `CacheTtlSeconds` |
| `StatisticsOptions` | `Statistics` | bucket thresholds + anomaly monitor settings |
| `JwtOptions` | `Jwt` | `Issuer`, `Audience`, `SigningKey`, `ExpiryMinutes` |

Each is registered in `Program.cs` with:

```csharp
builder.Services.AddOptions<WeatherApiOptions>()
    .Bind(builder.Configuration.GetSection(WeatherApiOptions.SectionName))
    .ValidateDataAnnotations()   // enforces [Required], [Range], [MinLength]
    .ValidateOnStart();          // fail fast at startup on misconfiguration
```

Options are consumed via `IOptions<T>` (constructor injection). The validation attributes live on
the Options classes, so a missing key or a too-short JWT signing key stops the app at startup with a
clear message instead of failing on the first request.

### Supplying secrets (do **not** commit real keys)

`appsettings.json` ships with placeholder values. Override them locally with **user-secrets**:

```bash
cd ApiAggregator
dotnet user-secrets init
dotnet user-secrets set "ExternalApis:Weather:ApiKey" "<your-openweathermap-key>"
dotnet user-secrets set "ExternalApis:News:ApiKey"    "<your-newsapi-key>"
dotnet user-secrets set "ExternalApis:GitHub:Token"   "<optional-github-pat>"
dotnet user-secrets set "Jwt:SigningKey"              "<a-long-random-secret-32+chars>"
```

…or with **environment variables** (double underscore = section separator):

```bash
export ExternalApis__Weather__ApiKey="..."
export Jwt__SigningKey="..."
```

The standard ASP.NET Core configuration precedence applies: env vars / user-secrets override
`appsettings.{Environment}.json`, which overrides `appsettings.json`.

---

## Running the service

```bash
# from the repository root
dotnet run --project ApiAggregator
```

In `Development`, Swagger UI is available at:

```
http://localhost:5183/swagger
```

Use the **Authorize** button in Swagger (top right) to paste a JWT and call the protected endpoints.

---

## Authentication

All endpoints except the token endpoint require a JWT bearer token.

1. Get a token (demo endpoint — accepts any non-empty username/password; it stands in for a real
   identity provider):

   ```bash
   curl -X POST http://localhost:5183/api/auth/token \
     -H "Content-Type: application/json" \
     -d '{"username":"alice","password":"pw"}'
   ```

   ```json
   { "accessToken": "eyJ...", "expiresAt": "2026-06-21T14:59:07Z", "tokenType": "Bearer" }
   ```

2. Send it on subsequent calls:

   ```bash
   curl "http://localhost:5183/api/aggregation?keyword=dotnet" \
     -H "Authorization: Bearer eyJ..."
   ```

---

## API reference

### `POST /api/auth/token`
Exchange credentials for a JWT.

- **Body:** `{ "username": string, "password": string }`
- **200:** `{ "accessToken": string, "expiresAt": datetime, "tokenType": "Bearer" }`
- **401:** empty username or password.

### `GET /api/aggregation`  *(auth required)*
Fetch, merge, filter, and sort data from all sources.

**Query parameters**

| Param | Type | Description |
|---|---|---|
| `city` | string | City for the weather provider (defaults to configured `DefaultCity`). |
| `keyword` | string | Search term for news + GitHub (defaults to each provider's configured query). |
| `sources` | string[] | Restrict to these sources, e.g. `sources=News&sources=GitHub`. |
| `category` | string | Restrict to a category: `weather`, `news`, or `repository`. |
| `fromDate` | datetime | Keep items dated on/after this. |
| `toDate` | datetime | Keep items dated on/before this. |
| `sortBy` | enum | `Date` \| `Relevance` \| `Source` \| `Title` (default `Relevance`). |
| `sortDir` | enum | `Ascending` \| `Descending` (default `Descending`). |

**Response `200`**

```json
{
  "items": [
    {
      "source": "GitHub",
      "category": "repository",
      "title": "dotnet/runtime",
      "description": ".NET runtime",
      "url": "https://github.com/dotnet/runtime",
      "date": "2024-05-01T10:00:00Z",
      "relevance": 15000,
      "extra": { "stars": "15000", "language": "C#" }
    }
  ],
  "totalCount": 1,
  "sources": [
    { "source": "Weather", "succeeded": false, "itemCount": 0, "fromCache": false, "error": "401 ..." },
    { "source": "News",    "succeeded": false, "itemCount": 0, "fromCache": false, "error": "401 ..." },
    { "source": "GitHub",  "succeeded": true,  "itemCount": 20, "fromCache": false, "error": null }
  ],
  "generatedAt": "2026-06-21T14:00:00Z"
}
```

The `sources` array is the fallback report: a down source is `succeeded: false` with an `error`,
while the rest still return data. The overall call is still `200`.

### `GET /api/statistics`  *(auth required)*
Per-API request statistics.

```json
[
  {
    "apiName": "GitHub",
    "totalRequests": 5,
    "failedRequests": 0,
    "averageResponseTimeMs": 142.3,
    "minResponseTimeMs": 95.1,
    "maxResponseTimeMs": 310.0,
    "buckets": { "fast": 1, "average": 3, "slow": 1 }
  }
]
```

### `GET /api/statistics/{apiName}`  *(auth required)*
Statistics for one API (e.g. `Weather`). `404` if it has no recorded requests.

---

## Caching, resilience & fallback

- **Caching** (`SourceProviderBase`): a successful fetch is cached per provider under a
  query-specific key for `CacheTtlSeconds`. Repeated identical queries are served from memory and
  make **no** external call — verifiable via the statistics endpoint (request count does not rise
  for cached hits).
- **Transient-failure handling**: each typed `HttpClient` uses
  `AddStandardResilienceHandler()`, which layers a rate limiter, total + per-attempt timeouts,
  automatic retries with backoff, and a circuit breaker.
- **Fallback** is two-layered:
  1. *Stale cache* — every successful fetch also stores a longer-lived "last known good" copy. If a
     later live fetch throws, the provider serves the stale copy instead of failing.
  2. *Source isolation* — if there is no stale copy either, `AggregationService` catches the
     exception, marks that source as failed in the response, and returns the other sources' data.

---

## Statistics & anomaly detection

- **Collection**: `StatisticsTrackingHandler` is the inner-most HTTP handler on every typed client.
  It times each outbound attempt (success or failure) and records it into `StatisticsService`.
- **Thread safety**: `StatisticsService` is a singleton. The outer map is a
  `ConcurrentDictionary`; each per-API entry guards its counters (totals, min/max, buckets, recent
  samples) with a short lock so readers always see a consistent snapshot. A concurrency test fires
  16 × 1000 records at one API and asserts an exact count.
- **Buckets** (configurable): `fast < 100ms`, `average 100–200ms`, `slow > 200ms`.
- **Anomaly monitor**: `PerformanceAnomalyMonitor` (a `BackgroundService`) runs every
  `AnomalyCheckIntervalSeconds` and logs a warning when an API's average over the last
  `AnomalyLookbackMinutes` exceeds its lifetime average by the `AnomalyRatioThreshold`
  (default 1.5× = 50% slower). The comparison logic lives in `StatisticsService.DetectAnomalies()`
  so it is unit-tested directly.

---

## Adding a new API

1. Add an `XyzApiOptions` class + a section in `appsettings.json`.
2. Add DTOs for the API's response under `Models/External`.
3. Create `XyzSourceProvider : SourceProviderBase`, implementing `SourceName`, `CacheTtl`,
   `BuildCacheKey`, and `FetchFromSourceAsync` (call the API, map to `AggregatedItem`s; tag the
   request with `StatisticsTrackingHandler.ApiNameKey`).
4. Register it in `Program.cs`:
   ```csharp
   var xyz = builder.Services.AddHttpClient<XyzSourceProvider>((sp, c) => { /* base address */ });
   AddResilienceAndStats(xyz);
   builder.Services.AddTransient<ISourceProvider>(sp => sp.GetRequiredService<XyzSourceProvider>());
   ```

That's it — the aggregator, caching, resilience, stats, and filtering all apply automatically.

---

## Tests

```bash
dotnet test
```

The `ApiAggregator.Tests` project (xUnit + Moq) covers:

- **`StatisticsServiceTests`** — counting/averaging, bucket boundaries, anomaly detection
  (using a controllable `TimeProvider`), and thread-safety under concurrent writers.
- **`AggregationServiceTests`** — parallel merge, failing-source isolation/fallback, filtering by
  source/category/date, and sorting by relevance/date.
- **`SourceProviderBaseTests`** — cache hits avoid re-fetching; stale-cache fallback on failure;
  exception propagation when nothing is cached.
- **`GitHubSourceProviderTests`** — JSON → `AggregatedItem` normalization (stub `HttpMessageHandler`)
  and non-success status handling.
- **`JwtTokenServiceTests`** — issued tokens validate with the expected claims and expiry.
```

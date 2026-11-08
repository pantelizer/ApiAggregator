# API Aggregator

An ASP.NET Core (.NET 10) service that fetches data from several external APIs in parallel and
returns it through one unified endpoint, with filtering and sorting.

- [Quick start](#quick-start)
- [External APIs](#external-apis)
- [Configuration](#configuration)
- [Endpoints](#endpoints)

---

## Quick start

**You need:** the [.NET 10 SDK](https://dotnet.microsoft.com/download).

**API keys:** an [OpenWeatherMap](https://openweathermap.org/api) key and a
[NewsAPI](https://newsapi.org/) key. Without them the app still runs: GitHub works with no key,
and Weather/News show as failed sources in the response. A GitHub token is optional (it only
raises the rate limit).

### Run

```bash
cd ApiAggregator
dotnet user-secrets set "ExternalApis:Weather:ApiKey" "<your-openweathermap-key>"
dotnet user-secrets set "ExternalApis:News:ApiKey" "<your-newsapi-key>"
dotnet run
```

Open **http://localhost:5183/swagger**.

### Alternative: edit `secrets.json` directly

Instead of `dotnet user-secrets set`, you can write the values into the project's `secrets.json`.
In Visual Studio, right-click the **ApiAggregator** project and choose **Manage User Secrets**.
You can also create the file yourself at:

- Windows: `%APPDATA%\Microsoft\UserSecrets\0ad16f95-586c-4375-bba0-f1d02a964104\secrets.json`
- macOS/Linux: `~/.microsoft/usersecrets/0ad16f95-586c-4375-bba0-f1d02a964104/secrets.json`

```json
{
  "ExternalApis:Weather:ApiKey": "<your-openweathermap-key>",
  "ExternalApis:News:ApiKey": "<your-newsapi-key>",
  "Jwt:SigningKey": "<32+ characters>"
}
```

A GitHub token is optional; add it as `"ExternalApis:GitHub:ApiKey": "<your-github-token>"`.

User-secrets are only loaded in the `Development` environment, which `dotnet run` uses by default.

### Try it

1. `POST /api/auth/token` with any username and password, e.g. `{"username":"test","password":"test"}`.
   Copy the `accessToken`.
2. Click **Authorize** (top right in Swagger) and paste the token.
3. `GET /api/aggregation`. Try `keyword=ai`, `city=Athens`, `githubQuery=gods&newsQuery=cats`,
   `sources=GitHub,Weather`, `sortBy=Date`, `toDate=2026-11-07`.
4. `GET /api/aggregation/sources` lists the sources and their own parameters.
5. `GET /api/statistics` shows request counts, average times and the fast/average/slow buckets.
6. Repeat a request: the sources now say `"fromCache": true` and the statistics counts don't rise.

The JWT signing key in `appsettings.json` is a placeholder. It can be overridden with
`dotnet user-secrets set "Jwt:SigningKey" "<32+ characters>"`.

---

## External APIs

| Source | API | Needs a key? | Produces |
|---|---|---|---|
| `Weather` | [OpenWeatherMap](https://openweathermap.org/api) current weather | Yes | one item: current conditions for a city |
| `News` | [NewsAPI](https://newsapi.org/) `/everything` | Yes | one item per article |
| `GitHub` | [GitHub REST](https://docs.github.com/en/rest) repository search | Optional (higher rate limit with a token) | one item per repository |

News sends its key in the `X-Api-Key` header and GitHub sends its token as
`Authorization: Bearer`. OpenWeatherMap only accepts its key as the `appid` query parameter.

Without keys, the Weather and News providers receive `401` from their APIs. The service still
returns the GitHub results and reports the other two as failed sources.

---

## Configuration

All settings are in `ApiAggregator/appsettings.json`. API keys and the JWT signing key ship as
placeholders and are supplied through user-secrets or environment variables.

| Section | Settings |
|---|---|
| `ExternalApis:Weather` | `BaseUrl`, `ApiKey`, `Units`, `DefaultCity`, `CacheTtlSeconds`, `TimeoutSeconds` |
| `ExternalApis:News` | `BaseUrl`, `ApiKey`, `DefaultQuery`, `PageSize`, `CacheTtlSeconds`, `TimeoutSeconds` |
| `ExternalApis:GitHub` | `BaseUrl`, `ApiKey` (optional token), `UserAgent`, `DefaultQuery`, `PageSize`, `CacheTtlSeconds`, `TimeoutSeconds` |
| `Caching` | `RedisConnectionString` (empty = in-memory only), `RedisInstanceName`, fail-safe and timeout durations |
| `Statistics` | `FastThresholdMs`, `SlowThresholdMs`, `AnomalyCheckIntervalSeconds`, `AnomalyLookbackMinutes`, `AnomalyRatioThreshold` |
| `Jwt` | `Issuer`, `Audience`, `SigningKey` (32+ characters), `ExpiryMinutes` |

Settings are validated at startup; an invalid value (for example a missing API key) stops the
app with a message naming the setting.

**User-secrets** (local development):

```bash
cd ApiAggregator
dotnet user-secrets set "ExternalApis:Weather:ApiKey" "<your-openweathermap-key>"
dotnet user-secrets set "ExternalApis:News:ApiKey"    "<your-newsapi-key>"
dotnet user-secrets set "ExternalApis:GitHub:ApiKey"  "<optional-github-token>"
dotnet user-secrets set "Jwt:SigningKey"              "<32+ characters>"
```

**Environment variables** (double underscore = section separator):

```bash
export ExternalApis__Weather__ApiKey="..."
export Jwt__SigningKey="..."
```

---

## Endpoints

All endpoints except `POST /api/auth/token` require a JWT in the `Authorization` header:

```
Authorization: Bearer <accessToken>
```

### `POST /api/auth/token`

Exchanges credentials for a JWT. Any non-empty username and password are accepted.

**Request body**

```json
{ "username": "test", "password": "test" }
```

**Response `200`**

```json
{ "accessToken": "eyJ...", "expiresAt": "2026-11-08T14:59:07Z", "tokenType": "Bearer" }
```

**Response `401`:** empty username or password.

### `GET /api/aggregation`

Fetches data from the sources in parallel, merges it, then filters and sorts it.

**Query parameters** (all optional)

| Param | Type | Description |
|---|---|---|
| `keyword` | string | Search term for News and GitHub (defaults to each source's `DefaultQuery`). |
| `sources` | string[] | Only call these sources, e.g. `sources=News,GitHub` or `sources=News&sources=GitHub`. Case-insensitive. An unknown name returns `400`. |
| `category` | string | `weather`, `news` or `repository`. |
| `fromDate` | datetime | Keep items dated on or after this. A date-only value (`2026-11-07`) starts at `00:00:00Z`. |
| `toDate` | datetime | Keep items dated on or before this. A date-only value (`2026-11-07`) includes the whole day. |
| `sortBy` | enum | `Date`, `Relevance`, `Source` or `Title` (default `Relevance`). |
| `sortDir` | enum | `Ascending` or `Descending` (default `Descending`). |
| `city` | string | **[Weather]** City for the weather source (default `DefaultCity`). |
| `newsQuery` | string | **[News]** Search term for News only; overrides `keyword`. |
| `githubQuery` | string | **[GitHub]** Search term for GitHub only; overrides `keyword`. |

Example: `GET /api/aggregation?githubQuery=gods&newsQuery=cats&sortBy=Date`

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
    { "source": "News",    "succeeded": true,  "itemCount": 0, "fromCache": true,  "error": null },
    { "source": "GitHub",  "succeeded": true,  "itemCount": 1, "fromCache": false, "error": null }
  ],
  "generatedAt": "2026-11-08T14:00:00Z"
}
```

| Field | Description |
|---|---|
| `items` | The merged, filtered and sorted items from all sources. |
| `totalCount` | Number of items returned. |
| `sources` | One entry per source called: whether it succeeded, how many items it returned, whether they came from the cache, and the error if it failed. A failed source does not fail the request. |
| `generatedAt` | When the response was built (UTC). |

**Response `400`:** an unknown source name, e.g. `?sources=GitHub,Twitter`, or an invalid value
such as `?toDate=hello`.

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "Sources": ["Unknown source(s): Twitter. Valid sources: Weather, News, GitHub."] }
}
```

### `GET /api/aggregation/sources`

Lists the available sources and the parameters each one accepts.

**Response `200`**

```json
[
  { "name": "Weather", "parameters": [ { "name": "city", "description": "...", "example": "Athens" } ] },
  { "name": "News",    "parameters": [ { "name": "newsQuery", "description": "...", "example": "cats" } ] },
  { "name": "GitHub",  "parameters": [ { "name": "githubQuery", "description": "...", "example": "gods" } ] }
]
```

### `GET /api/statistics`

Request statistics for every external API that has been called: total and failed requests,
average, minimum and maximum response time, and the requests grouped into performance buckets
(fast < 100ms, average 100–200ms, slow > 200ms, configurable under `Statistics`).

**Response `200`**

```json
[
  {
    "apiName": "GitHub",
    "totalRequests": 5,
    "failedRequests": 0,
    "averageResponseTimeMs": 142.3,
    "minResponseTimeMs": 95.1,
    "maxResponseTimeMs": 310.0,
    "buckets": {
      "fast":    { "range": "< 100ms",   "count": 1, "averageResponseTimeMs": 95.1 },
      "average": { "range": "100-200ms", "count": 3, "averageResponseTimeMs": 102.2 },
      "slow":    { "range": "> 200ms",   "count": 1, "averageResponseTimeMs": 310.0 }
    }
  }
]
```

### `GET /api/statistics/{apiName}`

The same statistics for one API, e.g. `GET /api/statistics/Weather`.

**Response `200`:** one object in the format above.
**Response `404`:** the API has no recorded requests yet.

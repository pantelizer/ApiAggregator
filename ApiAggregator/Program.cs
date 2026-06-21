using System.Text;
using ApiAggregator.BackgroundServices;
using ApiAggregator.Configuration;
using ApiAggregator.Infrastructure;
using ApiAggregator.Infrastructure.Auth;
using ApiAggregator.Services.Aggregation;
using ApiAggregator.Services.Providers;
using ApiAggregator.Services.Statistics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 1. Options architecture
//    Each external API's settings (and the JWT/statistics settings) are bound from
//    configuration into a strongly-typed Options class. ValidateDataAnnotations() enforces
//    the [Required]/[Range] rules, and ValidateOnStart() makes a misconfiguration fail fast
//    at startup instead of on the first request. Nothing sensitive is hard-coded in C#.
// ---------------------------------------------------------------------------
builder.Services.AddOptions<WeatherApiOptions>()
    .Bind(builder.Configuration.GetSection(WeatherApiOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<NewsApiOptions>()
    .Bind(builder.Configuration.GetSection(NewsApiOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<GitHubApiOptions>()
    .Bind(builder.Configuration.GetSection(GitHubApiOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<StatisticsOptions>()
    .Bind(builder.Configuration.GetSection(StatisticsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ---------------------------------------------------------------------------
// 2. Core services
// ---------------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddMemoryCache();

// TimeProvider is injected wherever "now" is needed (statistics windowing, token expiry),
// which keeps those components deterministic and unit-testable.
builder.Services.AddSingleton(TimeProvider.System);

// Singleton in-memory statistics store, shared across all requests.
builder.Services.AddSingleton<IStatisticsService, StatisticsService>();

// The DelegatingHandler that times outbound calls. Transient: each HttpClient gets its own.
builder.Services.AddTransient<StatisticsTrackingHandler>();

builder.Services.AddScoped<IAggregationService, AggregationService>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();

// ---------------------------------------------------------------------------
// 3. Typed HttpClients for each provider
//    - BaseAddress/headers come from the bound Options.
//    - AddStandardResilienceHandler() adds retry, total + per-attempt timeout, and a circuit
//      breaker (transient-failure handling + fallback when an API is unavailable).
//    - The stats handler is added AFTER resilience so it is the inner-most handler and therefore
//      times each individual attempt.
//    Each typed client is also exposed as an ISourceProvider so the aggregation service discovers
//      it automatically — adding a new API is "write a provider + register it here".
// ---------------------------------------------------------------------------
// Adds the resilience pipeline then the stats handler. Registration order = pipeline order
// (first added is outer-most), so the stats handler ends up inner-most and times each attempt.
static void AddResilienceAndStats(IHttpClientBuilder clientBuilder)
{
    clientBuilder.AddStandardResilienceHandler();
    clientBuilder.AddHttpMessageHandler<StatisticsTrackingHandler>();
}

var weatherClient = builder.Services.AddHttpClient<WeatherSourceProvider>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WeatherApiOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl);
});
AddResilienceAndStats(weatherClient);
builder.Services.AddTransient<ISourceProvider>(sp => sp.GetRequiredService<WeatherSourceProvider>());

var newsClient = builder.Services.AddHttpClient<NewsSourceProvider>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NewsApiOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl);
    // NewsAPI rejects requests without a recognizable User-Agent.
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ApiAggregator");
});
AddResilienceAndStats(newsClient);
builder.Services.AddTransient<ISourceProvider>(sp => sp.GetRequiredService<NewsSourceProvider>());

var gitHubClient = builder.Services.AddHttpClient<GitHubSourceProvider>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GitHubApiOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl);
    // GitHub requires a User-Agent and an explicit API version header.
    client.DefaultRequestHeaders.UserAgent.ParseAdd(opts.UserAgent);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    if (!string.IsNullOrWhiteSpace(opts.Token))
    {
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.Token);
    }
});
AddResilienceAndStats(gitHubClient);
builder.Services.AddTransient<ISourceProvider>(sp => sp.GetRequiredService<GitHubSourceProvider>());

// ---------------------------------------------------------------------------
// 4. Background anomaly monitor (optional requirement)
// ---------------------------------------------------------------------------
builder.Services.AddHostedService<PerformanceAnomalyMonitor>();

// ---------------------------------------------------------------------------
// 5. JWT bearer authentication (optional requirement)
// ---------------------------------------------------------------------------
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                 ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// 6. OpenAPI / Swagger UI (with a JWT "Authorize" button)
// ---------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "API Aggregator", Version = "v1" });

    // Define the "Bearer" scheme so Swagger UI shows an Authorize button.
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the JWT obtained from POST /api/auth/token."
    });

    // Require that scheme on operations. In Microsoft.OpenApi v2 a requirement references the
    // named scheme via OpenApiSecuritySchemeReference rather than an inline OpenApiReference.
    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", doc, null), new List<string>() }
    });

    // Surface our XML doc comments in the Swagger UI.
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// ---------------------------------------------------------------------------
// 7. HTTP pipeline
// ---------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    // Convenience: visiting the root in dev sends you straight to the Swagger UI.
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposed so a test project can reference the entry-point assembly if needed.
public partial class Program;

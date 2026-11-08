using System.Text;
using ApiAggregator.BackgroundServices;
using ApiAggregator.Configuration;
using ApiAggregator.Infrastructure.Auth;
using ApiAggregator.Infrastructure.Swagger;
using ApiAggregator.Services.Aggregation;
using ApiAggregator.Services.Providers;
using ApiAggregator.Services.Statistics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<StatisticsOptions>()
    .Bind(builder.Configuration.GetSection(StatisticsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<CachingOptions>()
    .Bind(builder.Configuration.GetSection(CachingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddControllers();

// Memory cache by default; Redis is added as L2 + backplane when a connection string is set.
var cachingOptions = builder.Configuration.GetSection(CachingOptions.SectionName).Get<CachingOptions>()
                     ?? new CachingOptions();
var fusionCache = builder.Services.AddFusionCache()
    .WithOptions(o =>
    {
        o.DistributedCacheCircuitBreakerDuration = cachingOptions.RedisCircuitBreakerDuration;
        o.BackplaneCircuitBreakerDuration = cachingOptions.RedisCircuitBreakerDuration;
    })
    .WithDefaultEntryOptions(new FusionCacheEntryOptions
    {
        Duration = TimeSpan.FromMinutes(1),
        IsFailSafeEnabled = true,
        FailSafeMaxDuration = cachingOptions.FailSafeMaxDuration,
        FailSafeThrottleDuration = cachingOptions.FailSafeThrottleDuration,
        FactorySoftTimeout = cachingOptions.FactorySoftTimeout,
        AllowBackgroundDistributedCacheOperations = true,
        AllowBackgroundBackplaneOperations = true,
        DistributedCacheHardTimeout = cachingOptions.RedisOperationTimeout
    });

if (!string.IsNullOrWhiteSpace(cachingOptions.RedisConnectionString))
{
    fusionCache
        .WithSerializer(new FusionCacheSystemTextJsonSerializer())
        .WithDistributedCache(new RedisCache(new RedisCacheOptions
        {
            Configuration = cachingOptions.RedisConnectionString,
            InstanceName = cachingOptions.RedisInstanceName
        }))
        .WithBackplane(new RedisBackplane(new RedisBackplaneOptions
        {
            Configuration = cachingOptions.RedisConnectionString
        }));
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IStatisticsService, StatisticsService>();
builder.Services.AddScoped<IAggregationService, AggregationService>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();

builder.Services.AddSourceProvider<WeatherSourceProvider, WeatherApiOptions>(WeatherApiOptions.SectionName);

builder.Services.AddSourceProvider<NewsSourceProvider, NewsApiOptions>(NewsApiOptions.SectionName,
    (client, _) => client.DefaultRequestHeaders.UserAgent.ParseAdd("ApiAggregator"));

builder.Services.AddSourceProvider<GitHubSourceProvider, GitHubApiOptions>(GitHubApiOptions.SectionName,
    (client, options) =>
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
        }
    });

builder.Services.AddHostedService<PerformanceAnomalyMonitor>();

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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "API Aggregator", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the JWT obtained from POST /api/auth/token."
    });

    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", doc, null), new List<string>() }
    });

    c.OperationFilter<SourceParametersOperationFilter>();

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

app.Logger.LogInformation(
    string.IsNullOrWhiteSpace(cachingOptions.RedisConnectionString)
        ? "Cache mode: in-memory only (L1). Set Caching:RedisConnectionString to add Redis."
        : "Cache mode: hybrid (L1 memory + L2 Redis with backplane).");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;

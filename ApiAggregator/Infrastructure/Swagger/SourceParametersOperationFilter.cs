using System.Reflection;
using System.Text.Json.Nodes;
using ApiAggregator.Services.Providers;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ApiAggregator.Infrastructure.Swagger;

[AttributeUsage(AttributeTargets.Method)]
public sealed class IncludeSourceParametersAttribute : Attribute;

// Adds each source's own query parameters to endpoints marked with [IncludeSourceParameters].
public sealed class SourceParametersOperationFilter : IOperationFilter
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SourceParametersOperationFilter(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.GetCustomAttribute<IncludeSourceParametersAttribute>() is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var providers = scope.ServiceProvider.GetServices<ISourceProvider>();

        operation.Parameters ??= new List<IOpenApiParameter>();
        foreach (var provider in providers)
        {
            foreach (var parameter in provider.Parameters)
            {
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = parameter.Name,
                    In = ParameterLocation.Query,
                    Required = false,
                    Description = $"[{provider.SourceName}] {parameter.Description}",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    Example = parameter.Example is null ? null : JsonValue.Create(parameter.Example)
                });
            }
        }
    }
}

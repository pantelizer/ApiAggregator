using ApiAggregator.Controllers;
using ApiAggregator.Models;
using ApiAggregator.Services.Aggregation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ApiAggregator.Tests;

public class AggregationControllerTests
{
    private static readonly IReadOnlyList<SourceInfo> Sources =
    [
        new() { Name = "Weather", Parameters = [new SourceParameter("city", "City")] },
        new() { Name = "News", Parameters = [] },
        new() { Name = "GitHub", Parameters = [] }
    ];

    private static (AggregationController Controller, Mock<IAggregationService> Service) Build(string queryString)
    {
        var service = new Mock<IAggregationService>();
        service.Setup(s => s.GetSources()).Returns(Sources);
        service.Setup(s => s.AggregateAsync(It.IsAny<AggregationQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AggregatedResponse { Items = [], TotalCount = 0, Sources = [] });

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddControllers().Services.BuildServiceProvider()
        };
        httpContext.Request.QueryString = new QueryString(queryString);

        var controller = new AggregationController(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        return (controller, service);
    }

    [Fact]
    public async Task Unknown_source_returns_400_listing_the_valid_sources()
    {
        var (controller, service) = Build("?Sources=GitHub,Twitter");

        var result = await controller.Get(new AggregationQuery { Sources = ["GitHub,Twitter"] }, CancellationToken.None);

        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsAssignableFrom<ObjectResult>(result.Result).Value);
        var message = Assert.Single(problem.Errors["Sources"]);
        Assert.Contains("Unknown source(s): Twitter", message);
        Assert.Contains("Valid sources: Weather, News, GitHub", message);

        service.Verify(s => s.AggregateAsync(It.IsAny<AggregationQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("github")]
    [InlineData("Weather,News")]
    [InlineData("")]
    public async Task Known_or_blank_sources_are_accepted(string sources)
    {
        var (controller, service) = Build($"?Sources={sources}");

        var result = await controller.Get(new AggregationQuery { Sources = [sources] }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        service.Verify(s => s.AggregateAsync(It.IsAny<AggregationQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Query_string_values_are_passed_to_the_sources()
    {
        var (controller, service) = Build("?city=Athens&newsQuery=cats");
        AggregationQuery? passed = null;
        service.Setup(s => s.AggregateAsync(It.IsAny<AggregationQuery>(), It.IsAny<CancellationToken>()))
            .Callback<AggregationQuery, CancellationToken>((q, _) => passed = q)
            .ReturnsAsync(new AggregatedResponse { Items = [], TotalCount = 0, Sources = [] });

        await controller.Get(new AggregationQuery(), CancellationToken.None);

        Assert.Equal("Athens", passed!.GetParameter("CITY"));
        Assert.Equal("cats", passed.GetParameter("newsQuery"));
    }

    [Fact]
    public void Sources_endpoint_returns_every_source_with_its_parameters()
    {
        var (controller, _) = Build("");

        var result = controller.GetSources();

        var sources = Assert.IsAssignableFrom<IReadOnlyList<SourceInfo>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new[] { "Weather", "News", "GitHub" }, sources.Select(s => s.Name));
        Assert.Equal("city", Assert.Single(sources[0].Parameters).Name);
    }
}

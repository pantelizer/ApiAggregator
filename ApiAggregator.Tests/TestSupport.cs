using System.Net;
using System.Text;

namespace ApiAggregator.Tests;

/// <summary>
/// A <see cref="TimeProvider"/> whose "now" can be set and advanced by tests, so any code that
/// depends on time (statistics windowing, token expiry) becomes fully deterministic.
/// </summary>
public sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public MutableTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
/// An <see cref="HttpMessageHandler"/> that returns a canned response (or throws), letting us
/// unit-test providers without real network calls.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public int CallCount { get; private set; }

    public StubHttpMessageHandler(HttpStatusCode statusCode, string jsonBody)
    {
        _responder = _ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
    }

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(_responder(request));
    }
}

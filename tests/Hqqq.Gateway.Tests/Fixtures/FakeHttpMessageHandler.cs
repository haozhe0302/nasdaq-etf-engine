using System.Net;
using System.Net.Http;

namespace Hqqq.Gateway.Tests.Fixtures;

/// <summary>
/// Records outbound requests and returns configurable responses.
/// Used to inject into the named "legacy" HttpClient for deterministic tests.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly List<HttpRequestMessage> _requests = [];
    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public FakeHttpMessageHandler()
    {
        _handler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        });
    }

    public IReadOnlyList<HttpRequestMessage> Requests => _requests;

    public void SetHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        => _handler = handler;

    public void SetResponse(HttpStatusCode status, string json)
    {
        _handler = (_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Add(request);
        return _handler(request, cancellationToken);
    }
}

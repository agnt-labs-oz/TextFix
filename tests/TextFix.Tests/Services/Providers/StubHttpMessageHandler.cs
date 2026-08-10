using System.Net;

namespace TextFix.Tests.Services.Providers;

/// <summary>
/// Returns a canned response, or throws a canned exception, for every request.
/// Records the last request body so tests can assert on the payload.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string? _body;
    private readonly Exception? _throw;
    private readonly TimeSpan _delay;

    public string? LastRequestBody { get; private set; }
    public HttpRequestMessage? LastRequest { get; private set; }

    public StubHttpMessageHandler(HttpStatusCode status, string body, TimeSpan? delay = null)
    {
        _status = status;
        _body = body;
        _delay = delay ?? TimeSpan.Zero;
    }

    public StubHttpMessageHandler(Exception toThrow) => _throw = toThrow;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        if (_throw is not null) throw _throw;

        if (_delay > TimeSpan.Zero)
            await Task.Delay(_delay, cancellationToken);

        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body!, System.Text.Encoding.UTF8, "application/json"),
        };
    }
}

using Hqqq.Gateway.Services.Sources;

namespace Hqqq.Gateway.Endpoints;

public static class QuoteEndpoints
{
    public static WebApplication MapQuoteEndpoints(this WebApplication app)
    {
        app.MapGet("/api/quote", async (IQuoteSource source, CancellationToken ct) =>
            await source.GetQuoteAsync(ct))
            .WithName("GetQuote")
            .WithTags("Quote");

        return app;
    }
}

namespace Hqqq.Gateway.Endpoints;

public static class GatewayEndpointsExtensions
{
    public static WebApplication MapGatewayEndpoints(this WebApplication app)
    {
        app.MapQuoteEndpoints();
        app.MapConstituentsEndpoints();
        app.MapHistoryEndpoints();
        app.MapSystemHealthEndpoints();
        return app;
    }
}

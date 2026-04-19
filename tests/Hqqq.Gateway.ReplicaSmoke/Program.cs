using System.Net.Http;
using System.Text.Json;
using Hqqq.Contracts.Dtos;
using Hqqq.Infrastructure.Redis;
using Hqqq.Infrastructure.Serialization;
using Microsoft.AspNetCore.SignalR.Client;
using StackExchange.Redis;

namespace Hqqq.Gateway.ReplicaSmoke;

// Phase 2D5 — operator-runnable end-to-end replica-smoke harness.
//
// Proves that two gateway replicas, both subscribed to the same Redis
// pub/sub channel (`hqqq:channel:quote-update`), each broadcast a single
// published QuoteUpdate to their own connected SignalR clients. This is the
// human-runnable equivalent of an integration test for the multi-gateway
// fan-out path; the in-process unit tests in
// tests/Hqqq.Gateway.Tests/Hubs/QuoteUpdateBroadcasterTests.cs cover the
// per-replica dispatch path, and this harness covers the topology.
//
// Exit codes:
//   0  — both replicas served REST probes and both SignalR clients
//        received the published QuoteUpdate within the timeout.
//   1  — anything else (REST failure, SignalR connect failure, missed
//        message on either client, payload mismatch).

internal static class Program
{
    private const string ChannelHubPath = "/hubs/market";

    public static async Task<int> Main()
    {
        var config = ReplicaSmokeConfig.FromEnvironment();
        config.PrintBanner();

        try
        {
            await ProbeRestAsync(config).ConfigureAwait(false);
        }
        catch (ReplicaSmokeException ex)
        {
            Console.Error.WriteLine($"[FAIL] REST probe failed: {ex.Message}");
            return 1;
        }

        var connectionA = BuildHubConnection(config.GatewayABaseUrl);
        var connectionB = BuildHubConnection(config.GatewayBBaseUrl);

        var receivedA = new TaskCompletionSource<QuoteUpdateDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedB = new TaskCompletionSource<QuoteUpdateDto>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Register handlers BEFORE StartAsync so a publish can never beat the
        // subscription registration on the client side.
        connectionA.On<QuoteUpdateDto>("QuoteUpdate", dto => receivedA.TrySetResult(dto));
        connectionB.On<QuoteUpdateDto>("QuoteUpdate", dto => receivedB.TrySetResult(dto));

        ConnectionMultiplexer? multiplexer = null;
        try
        {
            using var startCts = new CancellationTokenSource(config.Timeout);
            try
            {
                await Task.WhenAll(
                    connectionA.StartAsync(startCts.Token),
                    connectionB.StartAsync(startCts.Token)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine($"[FAIL] SignalR StartAsync timed out after {config.Timeout.TotalSeconds:0.#}s");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[FAIL] SignalR StartAsync threw: {ex.Message}");
                return 1;
            }

            Console.WriteLine("[PASS] SignalR clients connected to both replicas");

            try
            {
                multiplexer = await ConnectionMultiplexer.ConnectAsync(config.RedisConfiguration).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[FAIL] Redis connect failed ({config.RedisConfiguration}): {ex.Message}");
                return 1;
            }

            var (envelope, payload) = BuildEnvelope(config.BasketId);

            var subscriber = multiplexer.GetSubscriber();
            var channel = RedisChannel.Literal(RedisKeys.QuoteUpdateChannel);

            // PUBLISH returns the number of subscribers that received the
            // message. Both gateway replicas should be subscribed.
            var subscriberCount = await subscriber.PublishAsync(channel, payload).ConfigureAwait(false);
            Console.WriteLine($"[INFO] PUBLISH {RedisKeys.QuoteUpdateChannel} delivered to {subscriberCount} subscriber(s)");

            if (subscriberCount < 2)
            {
                Console.Error.WriteLine(
                    $"[FAIL] expected >= 2 Redis subscribers (one per gateway replica), got {subscriberCount}. " +
                    "Check that hqqq-gateway and hqqq-gateway-b are both running and healthy.");
                return 1;
            }

            using var receiveCts = new CancellationTokenSource(config.Timeout);
            try
            {
                await Task.WhenAll(receivedA.Task, receivedB.Task)
                    .WaitAsync(receiveCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine(
                    $"[FAIL] timed out after {config.Timeout.TotalSeconds:0.#}s waiting for QuoteUpdate. " +
                    $"received A={receivedA.Task.IsCompletedSuccessfully} B={receivedB.Task.IsCompletedSuccessfully}");
                return 1;
            }

            var dtoA = receivedA.Task.Result;
            var dtoB = receivedB.Task.Result;

            if (!PayloadsMatch(envelope.Update, dtoA, out var diffA))
            {
                Console.Error.WriteLine($"[FAIL] gateway-a payload mismatch: {diffA}");
                return 1;
            }
            if (!PayloadsMatch(envelope.Update, dtoB, out var diffB))
            {
                Console.Error.WriteLine($"[FAIL] gateway-b payload mismatch: {diffB}");
                return 1;
            }

            Console.WriteLine("[PASS] gateway-a received matching QuoteUpdate");
            Console.WriteLine("[PASS] gateway-b received matching QuoteUpdate");
            Console.WriteLine();
            Console.WriteLine("Replica-smoke result: OK");
            return 0;
        }
        finally
        {
            try { await connectionA.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
            try { await connectionB.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
            multiplexer?.Dispose();
        }
    }

    private static HubConnection BuildHubConnection(string baseUrl)
    {
        var hubUrl = baseUrl.TrimEnd('/') + ChannelHubPath;
        return new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .Build();
    }

    private static async Task ProbeRestAsync(ReplicaSmokeConfig config)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        await ProbeOneAsync(http, config.GatewayABaseUrl, "gateway-a").ConfigureAwait(false);
        await ProbeOneAsync(http, config.GatewayBBaseUrl, "gateway-b").ConfigureAwait(false);
    }

    private static async Task ProbeOneAsync(HttpClient http, string baseUrl, string label)
    {
        foreach (var path in new[] { "/healthz/ready", "/api/quote" })
        {
            var url = baseUrl.TrimEnd('/') + path;
            HttpResponseMessage response;
            try
            {
                response = await http.GetAsync(url).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new ReplicaSmokeException($"{label} {path}: {ex.Message}");
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new ReplicaSmokeException($"{label} {path}: HTTP {(int)response.StatusCode}");
                }
                Console.WriteLine($"[PASS] {label} {path}: HTTP {(int)response.StatusCode}");
            }
        }
    }

    private static (QuoteUpdateEnvelope envelope, string payload) BuildEnvelope(string basketId)
    {
        var update = new QuoteUpdateDto
        {
            Nav = 612.34m,
            NavChangePct = 1.25m,
            MarketPrice = 510m,
            PremiumDiscountPct = -16.7m,
            Qqq = 510m,
            QqqChangePct = 1.05m,
            BasketValueB = 0.0006m,
            AsOf = new DateTimeOffset(2026, 4, 19, 13, 30, 0, TimeSpan.Zero),
            LatestSeriesPoint = null,
            Movers = Array.Empty<MoverDto>(),
            Freshness = new FreshnessDto
            {
                SymbolsTotal = 3,
                SymbolsFresh = 3,
                SymbolsStale = 0,
                FreshPct = 100m,
            },
            Feeds = new FeedInfoDto
            {
                WebSocketConnected = false,
                FallbackActive = false,
                PricingActive = true,
                BasketState = "active",
                PendingActivationBlocked = false,
            },
            QuoteState = "live",
            IsLive = true,
            IsFrozen = false,
        };

        var envelope = new QuoteUpdateEnvelope { BasketId = basketId, Update = update };
        var payload = JsonSerializer.Serialize(envelope, HqqqJsonDefaults.Options);
        return (envelope, payload);
    }

    private static bool PayloadsMatch(QuoteUpdateDto sent, QuoteUpdateDto received, out string diff)
    {
        if (sent.Nav != received.Nav)
        {
            diff = $"nav sent={sent.Nav} received={received.Nav}";
            return false;
        }
        if (sent.QuoteState != received.QuoteState)
        {
            diff = $"quoteState sent={sent.QuoteState} received={received.QuoteState}";
            return false;
        }
        if (sent.AsOf != received.AsOf)
        {
            diff = $"asOf sent={sent.AsOf:O} received={received.AsOf:O}";
            return false;
        }
        if (sent.IsLive != received.IsLive)
        {
            diff = $"isLive sent={sent.IsLive} received={received.IsLive}";
            return false;
        }

        diff = string.Empty;
        return true;
    }
}

internal sealed record ReplicaSmokeConfig(
    string GatewayABaseUrl,
    string GatewayBBaseUrl,
    string RedisConfiguration,
    string BasketId,
    TimeSpan Timeout)
{
    public static ReplicaSmokeConfig FromEnvironment()
    {
        var a = ReadEnv("HQQQ_GATEWAY_A_BASE_URL", "http://localhost:5030");
        var b = ReadEnv("HQQQ_GATEWAY_B_BASE_URL", "http://localhost:5031");
        var redis = ReadEnv("Redis__Configuration", "localhost:6379");
        var basket = ReadEnv("Gateway__BasketId", "HQQQ");
        var timeoutSecRaw = ReadEnv("HQQQ_REPLICA_SMOKE_TIMEOUT_SECONDS", "15");

        if (!double.TryParse(timeoutSecRaw, System.Globalization.CultureInfo.InvariantCulture, out var timeoutSec)
            || timeoutSec <= 0)
        {
            timeoutSec = 15;
        }

        return new ReplicaSmokeConfig(a, b, redis, basket, TimeSpan.FromSeconds(timeoutSec));
    }

    public void PrintBanner()
    {
        Console.WriteLine("HQQQ replica-smoke harness (Phase 2D5)");
        Console.WriteLine($"  gateway-a       : {GatewayABaseUrl}");
        Console.WriteLine($"  gateway-b       : {GatewayBBaseUrl}");
        Console.WriteLine($"  redis           : {RedisConfiguration}");
        Console.WriteLine($"  basketId        : {BasketId}");
        Console.WriteLine($"  channel         : {RedisKeys.QuoteUpdateChannel}");
        Console.WriteLine($"  timeout         : {Timeout.TotalSeconds:0.#}s");
        Console.WriteLine();
    }

    private static string ReadEnv(string key, string fallback)
    {
        var v = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(v) ? fallback : v;
    }
}

internal sealed class ReplicaSmokeException : Exception
{
    public ReplicaSmokeException(string message) : base(message) { }
}

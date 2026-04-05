using System.Text.Json;
using Microsoft.Extensions.Options;
using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.MarketData.Contracts;

namespace Hqqq.Api.Modules.MarketData.Services;

/// <summary>
/// Determines the current NYSE market session state using a local
/// exchange-calendar JSON file for holidays and early closes.
/// </summary>
public sealed class MarketSessionService
{
    private readonly TimeZoneInfo _tz;
    private readonly HashSet<DateOnly> _holidays;
    private readonly Dictionary<DateOnly, TimeOnly> _earlyCloses;
    private readonly ILogger<MarketSessionService> _logger;

    private static readonly TimeOnly MarketOpen = new(9, 30);
    private static readonly TimeOnly MarketClose = new(16, 0);

    public TimeZoneInfo MarketTimeZone => _tz;

    public MarketSessionService(
        IOptions<PricingOptions> options,
        ILogger<MarketSessionService> logger)
    {
        _logger = logger;
        _holidays = new HashSet<DateOnly>();
        _earlyCloses = new Dictionary<DateOnly, TimeOnly>();
        _tz = ResolveTimeZone(options.Value.MarketTimeZone);

        var path = options.Value.NyseCalendarFilePath;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var cal = JsonSerializer.Deserialize<NyseCalendarData>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (cal is not null)
                {
                    foreach (var h in cal.Holidays)
                        if (DateOnly.TryParse(h, out var d)) _holidays.Add(d);

                    foreach (var ec in cal.EarlyCloses)
                        if (DateOnly.TryParse(ec.Date, out var d)
                            && TimeOnly.TryParse(ec.CloseTime, out var t))
                            _earlyCloses[d] = t;

                    logger.LogInformation(
                        "Loaded NYSE calendar: {Holidays} holidays, {EarlyCloses} early closes",
                        _holidays.Count, _earlyCloses.Count);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load NYSE calendar from {Path}", path);
            }
        }
        else
        {
            logger.LogWarning(
                "NYSE calendar not found at {Path}, using time-only session rules", path);
        }
    }

    public MarketSessionSnapshot GetCurrentSession()
        => GetSession(DateTimeOffset.UtcNow);

    public MarketSessionSnapshot GetSession(DateTimeOffset utcTime)
    {
        var et = TimeZoneInfo.ConvertTime(utcTime, _tz);
        var today = DateOnly.FromDateTime(et.DateTime);
        var time = TimeOnly.FromDateTime(et.DateTime);

        if (et.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return new MarketSessionSnapshot
            {
                State = "weekend",
                Label = "Weekend",
                IsRegularSessionOpen = false,
                IsTradingDay = false,
                NextOpenUtc = FindNextOpen(today),
            };
        }

        if (_holidays.Contains(today))
        {
            return new MarketSessionSnapshot
            {
                State = "holiday",
                Label = "NYSE Holiday",
                IsRegularSessionOpen = false,
                IsTradingDay = false,
                NextOpenUtc = FindNextOpen(today),
            };
        }

        var closeTime = _earlyCloses.TryGetValue(today, out var ec) ? ec : MarketClose;
        var isEarlyClose = _earlyCloses.ContainsKey(today);

        if (time < MarketOpen)
        {
            return new MarketSessionSnapshot
            {
                State = "pre_market",
                Label = "Pre-Market",
                IsRegularSessionOpen = false,
                IsTradingDay = true,
                NextOpenUtc = ToUtc(today, MarketOpen),
            };
        }

        if (time < closeTime)
        {
            return new MarketSessionSnapshot
            {
                State = "regular_open",
                Label = isEarlyClose
                    ? $"Regular Session (closes {closeTime:HH:mm})"
                    : "Regular Session",
                IsRegularSessionOpen = true,
                IsTradingDay = true,
                NextOpenUtc = null,
            };
        }

        if (isEarlyClose)
        {
            return new MarketSessionSnapshot
            {
                State = "early_close_closed",
                Label = "Early Close \u2014 Closed",
                IsRegularSessionOpen = false,
                IsTradingDay = true,
                NextOpenUtc = FindNextOpen(today),
            };
        }

        return new MarketSessionSnapshot
        {
            State = "after_hours",
            Label = "After Hours",
            IsRegularSessionOpen = false,
            IsTradingDay = true,
            NextOpenUtc = FindNextOpen(today),
        };
    }

    private DateTimeOffset? FindNextOpen(DateOnly fromDate)
    {
        var candidate = fromDate.AddDays(1);
        for (int i = 0; i < 14; i++)
        {
            if (candidate.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
                && !_holidays.Contains(candidate))
            {
                return ToUtc(candidate, MarketOpen);
            }
            candidate = candidate.AddDays(1);
        }
        return null;
    }

    private DateTimeOffset ToUtc(DateOnly date, TimeOnly time)
    {
        var dt = date.ToDateTime(time);
        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), _tz);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) when (id == "America/New_York")
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }
}

internal sealed class NyseCalendarData
{
    public string Timezone { get; set; } = "America/New_York";
    public List<string> Holidays { get; set; } = [];
    public List<NyseEarlyClose> EarlyCloses { get; set; } = [];
}

internal sealed class NyseEarlyClose
{
    public string Date { get; set; } = "";
    public string CloseTime { get; set; } = "13:00";
}

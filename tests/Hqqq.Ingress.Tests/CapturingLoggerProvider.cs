using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Hqqq.Ingress.Tests;

/// <summary>
/// Minimal in-memory <see cref="ILoggerProvider"/> used by tests that
/// need to assert on emitted log lines (e.g. the hybrid-mode warning
/// when a Tiingo API key is configured but ignored).
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentBag<LogEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);
    public void Dispose() { }

    private sealed class CapturingLogger : ILogger
    {
        private readonly string _category;
        private readonly ConcurrentBag<LogEntry> _sink;

        public CapturingLogger(string category, ConcurrentBag<LogEntry> sink)
        {
            _category = category;
            _sink = sink;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _sink.Add(new LogEntry(_category, logLevel, formatter(state, exception), exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}

internal readonly record struct LogEntry(
    string Category,
    LogLevel Level,
    string Message,
    Exception? Exception);

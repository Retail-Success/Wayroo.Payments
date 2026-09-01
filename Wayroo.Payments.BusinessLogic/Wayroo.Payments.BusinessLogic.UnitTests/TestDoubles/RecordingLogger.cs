using Microsoft.Extensions.Logging;

namespace Wayroo.Payments.BusinessLogic.UnitTests.TestDoubles;

/// <summary>
/// An <see cref="ILogger{T}"/> that keeps what was logged, including the structured properties.
/// </summary>
/// <remarks>
/// Exists so tests can assert on a log event's <i>properties</i> rather than its rendered text. That
/// matters here because two CloudWatch alarms match on a property
/// (<c>$.Properties.PaymentsSignal</c>, see <c>PaymentsLogSignals</c>) — the log line is a deployed
/// contract, not just diagnostics, and a refactor that drops the property would otherwise break the
/// alarm silently: the metric filter stops matching, the metric reports no data, and the alarm's
/// <c>TreatMissingData.NOT_BREACHING</c> reads that as healthy.
/// </remarks>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<RecordedLog> _entries = [];

    public IReadOnlyList<RecordedLog> Entries => _entries;

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NoOpScope.Instance;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);

        // The message-template values MEL hands over (FormattedLogValues). Read through the interface
        // rather than the concrete type, which is internal to the logging package.
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var pair in pairs)
                properties[pair.Key] = pair.Value;
        }

        _entries.Add(new RecordedLog(
            logLevel,
            formatter(state, exception),
            exception,
            properties));
    }

    /// <summary>Finds the single entry carrying the given payments signal.</summary>
    public RecordedLog EntryForSignal(string signal)
        => _entries.Single(entry =>
            entry.Properties.TryGetValue(Models.PaymentsLogSignals.PropertyName, out var value)
            && Equals(value, signal));

    public bool HasSignal(string signal)
        => _entries.Any(entry =>
            entry.Properties.TryGetValue(Models.PaymentsLogSignals.PropertyName, out var value)
            && Equals(value, signal));

    private sealed class NoOpScope : IDisposable
    {
        internal static readonly NoOpScope Instance = new();

        public void Dispose()
        {
        }
    }
}

internal sealed record RecordedLog(
    LogLevel Level,
    string Message,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Properties);

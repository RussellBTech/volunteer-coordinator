using Microsoft.Extensions.Logging;

namespace VolunteerCoordinator.IntegrationTests;

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _entries = [];
    private readonly object _gate = new();

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(string categoryName, string message, Exception? exception)
    {
        lock (_gate)
        {
            _entries.Add($"{categoryName}: {message}{exception}");
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly CapturingLoggerProvider _provider;
        private readonly string _categoryName;

        public CapturingLogger(CapturingLoggerProvider provider, string categoryName)
        {
            _provider = provider;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _provider.Add(_categoryName, formatter(state, exception), exception);
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

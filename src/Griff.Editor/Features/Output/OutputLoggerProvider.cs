using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Output;

/// <summary>Routes everything logged through ILogger into the Output panel.</summary>
public sealed class OutputLoggerProvider(OutputLog log) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new OutputLogger(log, ShortName(categoryName));

    public void Dispose()
    {
    }

    private static string ShortName(string category) => category[(category.LastIndexOf('.') + 1)..];

    private sealed class OutputLogger(OutputLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (exception is not null)
                message = $"{message}{Environment.NewLine}{exception}";

            log.Add(new LogEntry(DateTime.Now, logLevel, category, message));
        }
    }
}

using Microsoft.Extensions.Logging;

namespace NpgsqlRestTests.Setup
{
#pragma warning disable CS8633, CS8767
    public class EmptyLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) => throw new NotImplementedException();
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) { }
    }
}
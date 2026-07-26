using System.Diagnostics;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using OpenCode.TraceContext;

namespace OpenCode.TraceContext.Tests;

public class ActivityEnricherTests
{
    [Fact]
    public void Enricher_AppliesTraceIdAndSpanId_WhenActivityActive()
    {
        using var listener = TraceContext.AlwaysSample();
        using var activity = TraceContext.Source.StartActivity("enricher-test", ActivityKind.Internal)!;

        LogEvent? captured = null;
        var logger = new LoggerConfiguration()
            .Enrich.WithActivityTrace()
            .WriteTo.Sink(new DelegatingSink(e => captured = e))
            .CreateLogger();

        logger.Information("hello");

        Assert.NotNull(captured);
        Assert.Equal(activity.TraceId.ToHexString(), ((ScalarValue)captured!.Properties[TraceContextLoggerProperties.TraceIdPropertyName]).Value);
        Assert.Equal(activity.SpanId.ToHexString(), ((ScalarValue)captured.Properties[TraceContextLoggerProperties.SpanIdPropertyName]).Value);
    }

    [Fact]
    public void Enricher_Noop_WhenNoActivity()
    {
        LogEvent? captured = null;
        var logger = new LoggerConfiguration()
            .Enrich.WithActivityTrace()
            .WriteTo.Sink(new DelegatingSink(e => captured = e))
            .CreateLogger();

        logger.Information("hello");

        Assert.NotNull(captured);
        Assert.False(captured!.Properties.ContainsKey(TraceContextLoggerProperties.TraceIdPropertyName));
    }

    private sealed class DelegatingSink : ILogEventSink
    {
        private readonly Action<LogEvent> _write;
        public DelegatingSink(Action<LogEvent> write) => _write = write;
        public void Emit(LogEvent logEvent) => _write(logEvent);
    }
}
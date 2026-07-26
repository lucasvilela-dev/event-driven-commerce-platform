using System.Diagnostics;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace OpenCode.TraceContext;

public static class TraceContextLoggerProperties
{
    public const string TraceIdPropertyName = "TraceId";
    public const string SpanIdPropertyName = "SpanId";
    public const string ParentSpanIdPropertyName = "ParentSpanId";
}

public sealed class ActivityEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var current = Activity.Current;
        if (current is null) return;

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            TraceContextLoggerProperties.TraceIdPropertyName, current.TraceId.ToHexString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            TraceContextLoggerProperties.SpanIdPropertyName, current.SpanId.ToHexString()));

        var parent = current.Parent;
        if (parent is not null)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                TraceContextLoggerProperties.ParentSpanIdPropertyName, parent.SpanId.ToHexString()));
        }
    }
}

public static class LoggerEnricherConfigurationExtensions
{
    public static LoggerConfiguration WithActivityTrace(this LoggerEnrichmentConfiguration cfg)
        => cfg.With(new ActivityEnricher());
}
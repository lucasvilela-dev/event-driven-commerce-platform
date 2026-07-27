using System.Collections.Generic;
using System.Diagnostics;

namespace OpenCode.TraceContext;

public interface ITraceContextCarrier
{
    string? Get(string key);
    void Set(string key, string value);
}

public static class TraceContext
{
    public const string TraceparentHeader = "traceparent";
    public const string TracestateHeader = "tracestate";
    public const string ActivitySourceName = "OpenCode.TraceContext";

    private static readonly ActivitySource s_source = new(ActivitySourceName, "1.0");
#pragma warning disable CS8622
    private static readonly DistributedContextPropagator.PropagatorGetterCallback s_getter =
        (object c, string f, out string v, out IEnumerable<string> vs) =>
        {
            v = ((ITraceContextCarrier)c).Get(f)!;
            vs = null!;
        };
    private static readonly DistributedContextPropagator.PropagatorSetterCallback s_setter =
        (object c, string f, string v) => ((ITraceContextCarrier)c).Set(f, v);
#pragma warning restore CS8622

    public static ActivitySource Source => s_source;

    public static ActivityContext? ReadFromHttpHeaders(IReadOnlyDictionary<string, string?> headers)
        => Extract(new HttpHeaderCarrier(headers));

    public static void WriteToHttpHeaders(Activity activity, IDictionary<string, string?> headers)
        => DistributedContextPropagator.Current!.Inject(
            activity, new HttpHeaderCarrier(headers), s_setter);

    public static ActivityContext? ReadFromKafkaHeaders(IReadOnlyCollection<KeyValuePair<string, byte[]>> headers)
        => Extract(new KafkaHeaderCarrier(headers));

    public static void WriteToKafkaHeaders(Activity activity, ICollection<KeyValuePair<string, byte[]>> headers)
        => DistributedContextPropagator.Current!.Inject(
            activity, new KafkaHeaderCarrier(headers), s_setter);

    public static Activity? StartChild(string name, ActivityKind kind = ActivityKind.Internal) =>
        s_source.StartActivity(name, kind, Activity.Current?.Context ?? default);

    public static Activity? StartChildFromHttpHeaders(
        string name, IReadOnlyDictionary<string, string?> inbound, ActivityKind kind = ActivityKind.Server) =>
        s_source.StartActivity(name, kind, ReadFromHttpHeaders(inbound) ?? default);

    public static Activity? StartChildFromKafkaHeaders(
        string name, IReadOnlyCollection<KeyValuePair<string, byte[]>> inbound, ActivityKind kind = ActivityKind.Consumer) =>
        s_source.StartActivity(name, kind, ReadFromKafkaHeaders(inbound) ?? default);

    public static IDisposable AlwaysSample()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static ActivityContext? Extract(ITraceContextCarrier carrier)
    {
        DistributedContextPropagator.Current!.ExtractTraceIdAndState(
            carrier, s_getter, out var traceparent, out var tracestate);
        if (string.IsNullOrWhiteSpace(traceparent)) return null;
        return ActivityContext.TryParse(traceparent, tracestate, out var ctx) ? ctx : null;
    }
}
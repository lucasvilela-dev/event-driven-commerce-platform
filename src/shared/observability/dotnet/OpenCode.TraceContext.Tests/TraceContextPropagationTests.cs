using System.Collections.Generic;
using System.Diagnostics;
using OpenCode.TraceContext;

namespace OpenCode.TraceContext.Tests;

public class TraceContextPropagationTests
{
    private const string ValidTraceparent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    [Fact]
    public void Http_RoundTrips()
    {
        using var listener = TraceContext.AlwaysSample();
        using var activity = TraceContext.Source.StartActivity("test", ActivityKind.Internal)!;
        var outHeaders = new Dictionary<string, string?>();
        TraceContext.WriteToHttpHeaders(activity, outHeaders);
        Assert.Equal(activity.Id, outHeaders["traceparent"]);
    }

    [Fact]
    public void Http_ExtractsActivityContext()
    {
        var headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["traceparent"] = ValidTraceparent
        };
        var ctx = TraceContext.ReadFromHttpHeaders(headers);
        Assert.NotNull(ctx);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", ctx!.Value.TraceId.ToHexString());
        Assert.Equal("b7ad6b7169203331", ctx.Value.SpanId.ToHexString());
        Assert.True((ctx.Value.TraceFlags & ActivityTraceFlags.Recorded) != 0);
    }

    [Fact]
    public void Http_CaseInsensitive()
    {
        var headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["TraceParent"] = ValidTraceparent
        };
        Assert.NotNull(TraceContext.ReadFromHttpHeaders(headers));
    }

    [Fact]
    public void Http_Missing_ReturnsNull()
    {
        var headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Assert.Null(TraceContext.ReadFromHttpHeaders(headers));
    }

    [Fact]
    public void Http_Garbage_ReturnsNull()
    {
        var headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["traceparent"] = "not-a-traceparent"
        };
        Assert.Null(TraceContext.ReadFromHttpHeaders(headers));
    }

    [Fact]
    public void Http_ConsumesTraceState()
    {
        var headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["traceparent"] = ValidTraceparent,
            ["tracestate"] = "vendor=foo"
        };
        var ctx = TraceContext.ReadFromHttpHeaders(headers);
        Assert.NotNull(ctx);
        Assert.Equal("vendor=foo", ctx!.Value.TraceState);
    }

    [Fact]
    public void Kafka_RoundTrips()
    {
        using var listener = TraceContext.AlwaysSample();
        using var activity = TraceContext.Source.StartActivity("kafka-test", ActivityKind.Internal)!;
        var headers = new List<KeyValuePair<string, byte[]>>();
        TraceContext.WriteToKafkaHeaders(activity, headers);
        var ctx = TraceContext.ReadFromKafkaHeaders(headers);
        Assert.NotNull(ctx);
        Assert.Equal(activity.TraceId.ToHexString(), ctx!.Value.TraceId.ToHexString());
        Assert.Equal(activity.SpanId.ToHexString(), ctx.Value.SpanId.ToHexString());
    }

    [Fact]
    public void Kafka_MissingOrEmpty_ReturnsNull()
    {
        Assert.Null(TraceContext.ReadFromKafkaHeaders(new List<KeyValuePair<string, byte[]>>()));
        Assert.Null(TraceContext.ReadFromKafkaHeaders(
            new List<KeyValuePair<string, byte[]>> { new("traceparent", Array.Empty<byte>()) }));
    }

    [Fact]
    public void AlwaysSample_EnablesActivities()
    {
        using var listener = TraceContext.AlwaysSample();
        using var activity = TraceContext.StartChild("with-listener");
        Assert.NotNull(activity);
        Assert.False(string.IsNullOrEmpty(activity!.Id));
    }

    [Fact]
    public void StartChildFromHttpHeaders_LinksParent()
    {
        using var listener = TraceContext.AlwaysSample();
        var headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["traceparent"] = ValidTraceparent
        };
        using var activity = TraceContext.StartChildFromHttpHeaders("consume", headers);
        Assert.NotNull(activity);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", activity!.TraceId.ToHexString());
    }
}
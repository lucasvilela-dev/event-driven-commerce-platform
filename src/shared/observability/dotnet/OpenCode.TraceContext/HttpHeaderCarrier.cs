using System.Collections.Generic;

namespace OpenCode.TraceContext;

public sealed class HttpHeaderCarrier : ITraceContextCarrier
{
    private readonly Func<string, string?> _get;
    private readonly Action<string, string>? _set;

    public HttpHeaderCarrier(IReadOnlyDictionary<string, string?> headers)
        => _get = k => headers.TryGetValue(k, out var v) ? v : null;

    public HttpHeaderCarrier(IDictionary<string, string?> headers)
    {
        _get = k => headers.TryGetValue(k, out var v) ? v : null;
        _set = (k, v) => headers[k] = v;
    }

    public string? Get(string key) => _get(key);

    public void Set(string key, string value)
    {
        if (_set is null) throw new InvalidOperationException("Carrier is read-only");
        _set(key, value);
    }
}
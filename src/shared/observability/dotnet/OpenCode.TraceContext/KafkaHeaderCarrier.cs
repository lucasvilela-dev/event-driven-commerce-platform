using System.Collections.Generic;
using System.Text;

namespace OpenCode.TraceContext;

public sealed class KafkaHeaderCarrier : ITraceContextCarrier
{
    private readonly Func<string, string?> _get;
    private readonly Action<string, string>? _set;

    public KafkaHeaderCarrier(IReadOnlyCollection<KeyValuePair<string, byte[]>> headers)
        => _get = k => Lookup(headers, k);

    public KafkaHeaderCarrier(ICollection<KeyValuePair<string, byte[]>> headers)
    {
        _get = k => Lookup(headers, k);
        _set = (k, v) => headers.Add(new(k, Encoding.ASCII.GetBytes(v)));
    }

    public string? Get(string key) => _get(key);

    public void Set(string key, string value)
    {
        if (_set is null) throw new InvalidOperationException("Carrier is read-only");
        _set(key, value);
    }

    private static string? Lookup(IEnumerable<KeyValuePair<string, byte[]>> headers, string key)
    {
        foreach (var kv in headers)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                return kv.Value is null || kv.Value.Length == 0 ? null : Encoding.ASCII.GetString(kv.Value);
        }
        return null;
    }
}
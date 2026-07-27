package tracecontext

import (
	"crypto/rand"
	"strings"

	"go.opentelemetry.io/otel/trace"
)

type KV struct {
	Key   string
	Value []byte
}

type kafkaCarrier struct {
	headers []KV
}

func (c *kafkaCarrier) Get(key string) string {
	for _, h := range c.headers {
		if strings.EqualFold(h.Key, key) && len(h.Value) > 0 {
			return string(h.Value)
		}
	}
	return ""
}

func (c *kafkaCarrier) Set(key, value string) {
	c.headers = append(c.headers, KV{Key: key, Value: []byte(value)})
}

func (c *kafkaCarrier) Keys() []string {
	out := make([]string, 0, len(c.headers))
	for _, h := range c.headers {
		out = append(out, h.Key)
	}
	return out
}

func NewRootSpanContext() trace.SpanContext {
	var tid trace.TraceID
	var sid trace.SpanID
	_, _ = rand.Read(tid[:])
	_, _ = rand.Read(sid[:])
	return trace.NewSpanContext(trace.SpanContextConfig{
		TraceID:    tid,
		SpanID:     sid,
		TraceFlags: trace.FlagsSampled,
		Remote:     true,
	})
}
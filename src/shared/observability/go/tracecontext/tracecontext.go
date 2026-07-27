package tracecontext

import (
	"context"
	"net/http"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/propagation"
	"go.opentelemetry.io/otel/trace"
)

const (
	TraceparentHeader = "traceparent"
	TracestateHeader  = "tracestate"
)

var defaultPropagator propagation.TextMapPropagator = propagation.TraceContext{}

func FromHTTPHeaders(ctx context.Context, h http.Header) (trace.SpanContext, bool) {
	return extract(ctx, httpCarrier{h: h})
}

func IntoHTTPHeaders(ctx context.Context, sc trace.SpanContext, h http.Header) {
	inject(ctx, sc, httpCarrier{h: h})
}

func FromKafkaHeaders(ctx context.Context, h []KV) (trace.SpanContext, bool) {
	return extract(ctx, &kafkaCarrier{headers: h})
}

func IntoKafkaHeaders(ctx context.Context, sc trace.SpanContext, h *[]KV) {
	c := &kafkaCarrier{headers: *h}
	inject(ctx, sc, c)
	*h = c.headers
}

func SpanContextFromContext(ctx context.Context) (trace.SpanContext, bool) {
	sc := trace.SpanContextFromContext(ctx)
	return sc, sc.IsValid()
}

func ContextWithSpanContext(ctx context.Context, sc trace.SpanContext) context.Context {
	return trace.ContextWithRemoteSpanContext(ctx, sc)
}

func SetPropagator(p propagation.TextMapPropagator) {
	otel.SetTextMapPropagator(p)
	defaultPropagator = p
}

func extract(ctx context.Context, c propagation.TextMapCarrier) (trace.SpanContext, bool) {
	ctx = defaultPropagator.Extract(ctx, c)
	sc := trace.SpanContextFromContext(ctx)
	return sc, sc.IsValid()
}

func inject(ctx context.Context, sc trace.SpanContext, c propagation.TextMapCarrier) {
	ctx = trace.ContextWithRemoteSpanContext(ctx, sc)
	defaultPropagator.Inject(ctx, c)
}
package tracecontext

import (
	"context"
	"net/http"
	"testing"

	"go.opentelemetry.io/otel/trace"
)

const validTraceparent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"

func mustParseSC(t *testing.T) trace.SpanContext {
	t.Helper()
	h := http.Header{}
	h.Set("traceparent", validTraceparent)
	sc, ok := FromHTTPHeaders(context.Background(), h)
	if !ok {
		t.Fatal("parse traceparent")
	}
	return sc
}

func TestHTTP_RoundTrip(t *testing.T) {
	sc := mustParseSC(t)
	h := http.Header{}
	IntoHTTPHeaders(context.Background(), sc, h)
	if got := h.Get("traceparent"); got != validTraceparent {
		t.Errorf("round-trip: want %s, got %s", validTraceparent, got)
	}
	out, ok := FromHTTPHeaders(context.Background(), h)
	if !ok {
		t.Fatal("extract failed")
	}
	if out.TraceID().String() != sc.TraceID().String() {
		t.Errorf("trace id mismatch: want %s, got %s", sc.TraceID().String(), out.TraceID().String())
	}
	if out.SpanID().String() != sc.SpanID().String() {
		t.Errorf("span id mismatch: want %s, got %s", sc.SpanID().String(), out.SpanID().String())
	}
	if !out.IsSampled() {
		t.Error("expected sampled")
	}
}

func TestHTTP_CaseInsensitive(t *testing.T) {
	h := http.Header{}
	h.Set("TraceParent", validTraceparent)
	if _, ok := FromHTTPHeaders(context.Background(), h); !ok {
		t.Fatal("expected case-insensitive read")
	}
}

func TestHTTP_Missing(t *testing.T) {
	if _, ok := FromHTTPHeaders(context.Background(), http.Header{}); ok {
		t.Error("expected not ok on missing header")
	}
}

func TestHTTP_Garbage(t *testing.T) {
	h := http.Header{}
	h.Set("traceparent", "not-a-traceparent")
	if _, ok := FromHTTPHeaders(context.Background(), h); ok {
		t.Error("expected not ok on garbage")
	}
}

func TestKafka_RoundTrip(t *testing.T) {
	sc := mustParseSC(t)
	var headers []KV
	IntoKafkaHeaders(context.Background(), sc, &headers)
	if len(headers) != 1 || headers[0].Key != "traceparent" {
		t.Fatalf("unexpected headers: %#v", headers)
	}
	if string(headers[0].Value) != validTraceparent {
		t.Errorf("round-trip: want %s, got %s", validTraceparent, string(headers[0].Value))
	}
	out, ok := FromKafkaHeaders(context.Background(), headers)
	if !ok {
		t.Fatal("extract failed")
	}
	if out.TraceID().String() != sc.TraceID().String() {
		t.Errorf("trace id mismatch")
	}
}

func TestKafka_MissingOrEmpty(t *testing.T) {
	if _, ok := FromKafkaHeaders(context.Background(), nil); ok {
		t.Error("nil headers should not be ok")
	}
	if _, ok := FromKafkaHeaders(context.Background(), []KV{{Key: "traceparent", Value: nil}}); ok {
		t.Error("empty value should not be ok")
	}
}

func TestNewRootSpanContext_NonZeroSampled(t *testing.T) {
	sc := NewRootSpanContext()
	if !sc.IsValid() {
		t.Fatal("expected valid span context")
	}
	if !sc.IsSampled() {
		t.Fatal("expected sampled flag")
	}
	if sc.TraceID() == (trace.TraceID{}) {
		t.Error("trace id is zero")
	}
	if sc.SpanID() == (trace.SpanID{}) {
		t.Error("span id is zero")
	}
}

func TestSpanContextFromContext_RoundTrip(t *testing.T) {
	sc := mustParseSC(t)
	ctx := ContextWithSpanContext(context.Background(), sc)
	got, ok := SpanContextFromContext(ctx)
	if !ok {
		t.Fatal("expected ok")
	}
	if got.TraceID().String() != sc.TraceID().String() {
		t.Errorf("trace id mismatch")
	}
}
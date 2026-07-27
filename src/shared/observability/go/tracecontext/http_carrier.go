package tracecontext

import "net/http"

type httpCarrier struct {
	h http.Header
}

func (c httpCarrier) Get(key string) string { return c.h.Get(key) }

func (c httpCarrier) Set(key, value string) { c.h.Set(key, value) }

func (c httpCarrier) Keys() []string {
	out := make([]string, 0, len(c.h))
	for k := range c.h {
		out = append(out, k)
	}
	return out
}
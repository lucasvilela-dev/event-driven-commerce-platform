# AGENTS.md — src/api-gateway/

The single entry point for external HTTP clients. Implemented with **YARP**
(Yet Another Reverse Proxy) on .NET 10.

## Stack

- .NET 10 (`net10.0`)
- `Microsoft.ReverseProxy` (YARP)
- `Microsoft.AspNetCore.Authentication.JwtBearer` — validates JWT issued by
  the Identity service (no token issued here)
- Serilog → Seq (ADR-010)
- Polly for outbound retry/circuit breaker when proxying to backend services

## Responsibilities (and non-responsibilities)

- Route requests to `identity`, `product`, `order`, `payment` services.
- Validate JWT on protected routes; pass `X-User-Id`, `traceparent` downstream
  (W3C Trace Context, ADR-010).
- Rate limiting + request ID propagation; generate `traceparent` if inbound
  request has none.
- **No** business logic. **No** DB access. **No** Kafka producer/consumer.

## Config

Routes are declared in `appsettings.json` under `ReverseProxy:Routes` and
`ReverseProxy:Clusters`. Keep the route map declarative — avoid C# code that
dynamically rewrites routes.

## Mandatory ADRs

- ADR-010 (observability — propagation of `traceparent`)
- ADR-001 (.NET 10 conventions)

## Commands (once the service has a .csproj)

```powershell
dotnet build
dotnet run                         # listens on http://localhost:5000
dotnet test
```
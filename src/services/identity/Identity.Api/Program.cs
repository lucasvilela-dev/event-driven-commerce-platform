using Identity.Application;
using Identity.Infrastructure;
using OpenCode.TraceContext;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ServiceName", "identity")
    .Enrich.WithActivityTrace()
    .WriteTo.Console()
    .WriteTo.Seq(ctx.Configuration.GetConnectionString("Seq") ?? "http://localhost:8082"));

builder.Services.AddIdentityApplication();
builder.Services.AddIdentityInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

TraceContext.AlwaysSample();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
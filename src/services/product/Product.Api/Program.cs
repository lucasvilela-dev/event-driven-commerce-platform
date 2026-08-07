using OpenCode.TraceContext;
using Product.Application;
using Product.Infrastructure;
using Product.Projections;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ServiceName", "product")
    .Enrich.WithActivityTrace()
    .WriteTo.Console()
    .WriteTo.Seq(ctx.Configuration.GetConnectionString("Seq") ?? "http://localhost:8082"));

builder.Services.AddProductApplication();
builder.Services.AddProductInfrastructure(builder.Configuration);
builder.Services.AddProductProjections();

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

TraceContext.AlwaysSample();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
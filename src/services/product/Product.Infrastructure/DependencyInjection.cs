using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Product.Application.Abstractions;
using Product.Application.Options;
using Product.Infrastructure.Persistence;
using Product.Infrastructure.Persistence.Repositories;
using Product.Infrastructure.Services;
using StackExchange.Redis;

namespace Product.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProductInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));

        services.AddDbContext<ProductDbContext>(opts =>
            opts.UseNpgsql(configuration.GetConnectionString("Product"),
                npgsql => npgsql.MigrationsAssembly(typeof(ProductDbContext).Assembly.FullName)));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();

        var redisOpts = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions?>()
                        ?? new RedisOptions();
        if (!string.IsNullOrEmpty(redisOpts.ConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(sp =>
                ConnectionMultiplexer.Connect(redisOpts.ConnectionString));
        }

        return services;
    }
}
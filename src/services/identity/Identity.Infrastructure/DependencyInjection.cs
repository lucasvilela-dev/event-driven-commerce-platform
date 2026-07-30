using Identity.Application.Abstractions;
using Identity.Application.Options;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtIssuerOptions>(configuration.GetSection(JwtIssuerOptions.SectionName));

        services.AddDbContext<IdentityDbContext>(opts =>
            opts.UseNpgsql(configuration.GetConnectionString("Identity"),
                npgsql => npgsql.MigrationsAssembly(typeof(IdentityDbContext).Assembly.FullName)));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();

        services.AddSingleton(sp =>
        {
            var jwtOpts = sp.GetRequiredService<IOptions<JwtIssuerOptions>>().Value;
            return new SigningKeyProvider(jwtOpts.SigningKeyPath);
        });

        services.AddSingleton<ITokenIssuer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<JwtIssuerOptions>>();
            var keyProvider = sp.GetRequiredService<SigningKeyProvider>();
            return new TokenIssuer(opts, keyProvider);
        });

        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();

        return services;
    }
}
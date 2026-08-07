using Microsoft.Extensions.DependencyInjection;

namespace Product.Projections;

public static class DependencyInjection
{
    public static IServiceCollection AddProductProjections(this IServiceCollection services)
    {
        return services;
    }
}
using Helrift.Gate.Adapters.Firebase;
using Helrift.Gate.App.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Helrift.Gate;

public static class MarketplaceServiceCollectionExtensions
{
    public static IServiceCollection AddMarketplace(this IServiceCollection services)
    {
        services.AddSingleton<IMarketplaceRepository, FirebaseMarketplaceRepository>();
        services.AddScoped<Helrift.Gate.Api.Services.Marketplace.IMarketplaceService, Helrift.Gate.Api.Services.Marketplace.MarketplaceService>();
        return services;
    }
}

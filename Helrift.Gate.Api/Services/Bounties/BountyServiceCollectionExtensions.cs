using Helrift.Gate.Adapters.Firebase;
using Helrift.Gate.Api.Services.Bounties;
using Helrift.Gate.App.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Helrift.Gate;

public static class BountyServiceCollectionExtensions
{
    public static IServiceCollection AddBounties(this IServiceCollection services)
    {
        services.AddSingleton<IBountyRepository, FirebaseBountyRepository>();
        services.AddScoped<IBountyService, BountyService>();
        services.AddHostedService<BountyExpiryHostedService>();
        return services;
    }
}

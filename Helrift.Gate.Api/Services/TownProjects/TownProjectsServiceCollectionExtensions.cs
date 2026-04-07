// Services/TownProjects/TownProjectsServiceCollectionExtensions.cs
using Helrift.Gate.Adapters.Firebase;
using Helrift.Gate.Api.Services.TownProjects;
using Helrift.Gate.App.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Helrift.Gate;

public static class TownProjectsServiceCollectionExtensions
{
    public static IServiceCollection AddTownProjects(this IServiceCollection services)
    {
        // Repositories
        services.AddSingleton<ITownProjectConfigRepository, FirebaseTownProjectConfigRepository>();
        services.AddSingleton<ITownProjectStateRepository, FirebaseTownProjectStateRepository>();

        // Services
        services.AddSingleton<ITownProjectConfigService, TownProjectConfigService>();
        services.AddSingleton<ITownProjectStateService, TownProjectStateService>();
        services.AddSingleton<ITownProjectContributionService, TownProjectContributionService>();
        services.AddScoped<ITownProjectRewardService, TownProjectRewardService>();
        services.AddScoped<ITownProjectRotationService, TownProjectRotationService>();
        services.AddHostedService<TownProjectRotationHostedService>();

        return services;
    }
}
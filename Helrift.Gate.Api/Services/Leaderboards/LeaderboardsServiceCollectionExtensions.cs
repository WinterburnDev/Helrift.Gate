using Helrift.Gate.Adapters.Firebase.Leaderboards;
using Helrift.Gate.Api.Services.Accounts;
using Helrift.Gate.Services.Leaderboards;
using Microsoft.Extensions.DependencyInjection;

namespace Helrift.Gate;

public static class LeaderboardsServiceCollectionExtensions
{
    public static IServiceCollection AddLeaderboards(this IServiceCollection services)
    {
        // Core leaderboards
        services.AddSingleton<ILeaderboardService, LeaderboardService>();

        // Repo
        services.AddSingleton<ILeaderboardRepository, FirebaseLeaderboardRepository>();

        // Directory
        services.AddSingleton<ICharacterDirectoryService, CharacterDirectoryService>();

        return services;
    }
}

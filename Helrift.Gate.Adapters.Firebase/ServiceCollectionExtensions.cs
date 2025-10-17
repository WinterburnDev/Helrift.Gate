using Helrift.Gate.App;
using Microsoft.Extensions.DependencyInjection;

namespace Helrift.Gate.Adapters.Firebase;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFirebaseAdapters(
        this IServiceCollection services,
        FirebaseOptions options)
    {
        services.AddSingleton(options);
        services.AddScoped<IGameDataProvider, FirebaseGameDataProvider>();
        return services;
    }
}

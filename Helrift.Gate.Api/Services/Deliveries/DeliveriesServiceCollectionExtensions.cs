using Helrift.Gate.Adapters.Firebase;
using Helrift.Gate.Api.Services.Deliveries;
using Helrift.Gate.App.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Helrift.Gate;

public static class DeliveriesServiceCollectionExtensions
{
    public static IServiceCollection AddDeliveries(this IServiceCollection services)
    {
        services.AddSingleton<IDeliveryRepository, FirebaseDeliveryRepository>();
        services.AddScoped<IDeliveryService, DeliveryService>();
        return services;
    }
}
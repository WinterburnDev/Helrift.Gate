using Helrift.Gate.Adapters.Firebase;
using Helrift.Gate.Api.Services.Escrow;
using Helrift.Gate.App.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Helrift.Gate;

public static class EscrowServiceCollectionExtensions
{
    public static IServiceCollection AddEscrow(this IServiceCollection services)
    {
        services.AddSingleton<IEscrowRepository, FirebaseEscrowRepository>();
        services.AddSingleton<IGameServerEscrowBridge, GameServerEscrowBridge>();
        services.AddScoped<IEscrowBalanceService, CharacterEscrowBalanceService>();
        services.AddScoped<IEscrowService, EscrowService>();
        return services;
    }
}
// Services/Steam/SteamServerBootstrap.cs
using Helrift.Gate.Api.Services.Steam;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Steamworks;

public sealed class SteamServerBootstrap : BackgroundService
{
    private readonly ILogger<SteamServerBootstrap> _log;
    private readonly SteamServerOptions _opt;
    private volatile bool _initialized;

    public SteamServerBootstrap(ILogger<SteamServerBootstrap> log, IOptions<SteamServerOptions> opt)
    { _log = log; _opt = opt.Value; }

    public override Task StartAsync(CancellationToken ct)
    {
        try
        {
            _log.LogInformation("Initializing Facepunch SteamServer...");
            var init = new SteamServerInit("helrift_gate", "Helrift Gate Auth")
            {
                IpAddress = System.Net.IPAddress.Any,
                Secure = _opt.Secure,
                VersionString = _opt.VersionString,
                GamePort = _opt.GamePort,
                QueryPort = _opt.QueryPort
            };

            SteamServer.Init((AppId)_opt.AppId, init, false);
            SteamServer.LogOnAnonymous();
            _initialized = true;
            _log.LogInformation("SteamServer initialized + anonymous logon OK.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SteamServer init failed.");
        }
        return base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_initialized) SteamServer.RunCallbacks();
            await Task.Delay(15, stoppingToken);
        }
    }

    public override Task StopAsync(CancellationToken ct)
    {
        try
        {
            if (_initialized)
            {
                _log.LogInformation("Shutting down SteamServer...");
                SteamServer.LogOff();
                SteamServer.Shutdown();
                _initialized = false;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SteamServer shutdown failed.");
        }
        return base.StopAsync(ct);
    }
}
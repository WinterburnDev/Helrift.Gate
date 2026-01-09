using Google.Apis.Auth.OAuth2;
using Helrift.Gate;
using Helrift.Gate.Adapters.Firebase;
using Helrift.Gate.Api.Services;
using Helrift.Gate.Api.Services.Accounts;
using Helrift.Gate.Api.Services.Auth;
using Helrift.Gate.Api.Services.Friends;
using Helrift.Gate.Api.Services.GameServers;
using Helrift.Gate.Api.Services.Routing;
using Helrift.Gate.Api.Services.Steam;
using Helrift.Gate.Api.Services.Tokens;
using Helrift.Gate.App.Repositories;
using Helrift.Gate.Infrastructure;
using Helrift.Gate.Infrastructure.Parties;
using Helrift.Gate.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.Extensions.Http;
using System.Text;

// ----------------------
// Host + Services
// ----------------------
var builder = WebApplication.CreateBuilder(args);

// FIREBASE
var fbOptions = new FirebaseOptions
{
    DatabaseUrl = builder.Configuration["Firebase:DatabaseUrl"]
        ?? throw new InvalidOperationException("Missing config: Firebase:DatabaseUrl"),
    Timeout = TimeSpan.FromSeconds(8)
};
builder.Services.AddSingleton(fbOptions);

// GOOGLE AUTH
builder.Services.AddSingleton(sp =>
{
    var saPath = builder.Configuration["Firebase:ServiceAccountJsonPath"]!;
    return GoogleCredential
        .FromFile(saPath)
        .CreateScoped(
            "https://www.googleapis.com/auth/firebase.database",
            "https://www.googleapis.com/auth/userinfo.email");
});
builder.Services.AddTransient<GoogleAuthDelegatingHandler>();

// FIREBASE AUTH
builder.Services.AddHttpClient("firebase-admin", c =>
{
    c.BaseAddress = new Uri(fbOptions.DatabaseUrl);   // e.g. https://<project>-default-rtdb.firebaseio.com/
    c.Timeout = fbOptions.Timeout;
})
.AddHttpMessageHandler<GoogleAuthDelegatingHandler>()
.AddPolicyHandler(HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(150 * Math.Pow(2, i))));

// 5) MVC + Swagger
builder.Services.AddControllers(options =>
{
    // Do NOT infer [Required] from non-nullable reference types
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
}).SetCompatibilityVersion(CompatibilityVersion.Version_3_0);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// JWT
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<JwtJoinOptions>(builder.Configuration.GetSection("JoinJwt"));

// STEAM
builder.Services.Configure<SteamServerOptions>(builder.Configuration.GetSection("SteamServer"));
builder.Services.AddHostedService<SteamServerBootstrap>();
builder.Services.AddSingleton<ISteamAuthVerifier, FacepunchAuthVerifier>();

// GAME SERVERS
builder.Services.Configure<GameServersOptions>(builder.Configuration.GetSection("GameServers"));
builder.Services.AddSingleton<IGameServerDirectory, ConfigGameServerDirectory>();
builder.Services.AddSingleton<IJoinTokenService, Hs256JoinTokenService>();
builder.Services.AddSingleton<IReservationClient, HttpReservationClient>();

// DATA PROVIDERS
builder.Services.AddScoped<IGameDataProvider, FirebaseGameDataProvider>();
builder.Services.AddSingleton<IGuildDataProvider, FirebaseGuildDataProvider>();
builder.Services.AddSingleton<IMerchantDataProvider, FirebaseMerchantDataProvider>();
builder.Services.AddSingleton<IEntitlementsDataProvider, FirebaseEntitlementsDataProvider>();
builder.Services.AddSingleton<IPartyDataProvider, InMemoryPartyRepository>();
builder.Services.AddSingleton<IBanRepository, FirebaseBanRepository>();
builder.Services.AddSingleton<IAdminRepository, FirebaseAdminRepository>();

// SERVICES
builder.Services.AddSingleton<IGameServerConnectionRegistry, GameServerConnectionRegistry>();
builder.Services.AddSingleton<IAccountService, AccountService>();
builder.Services.AddSingleton<IFriendsService, FriendsService>();
builder.Services.AddSingleton<IChatBroadcaster, WebSocketChatBroadcaster>();
builder.Services.AddSingleton<WebSocketFriendNotifier>();
builder.Services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddSingleton<IPresenceService, PresenceService>();
builder.Services.AddSingleton<PartyPresenceCleanupListener>();
builder.Services.AddSingleton<IRealmService, RealmService>();

// PARTY
builder.Services.AddSingleton<WebSocketPartyNotifier>();
builder.Services.AddSingleton<IPartyService, PartyService>();
builder.Services.AddSingleton<IPartyExperienceBroadcaster, WebSocketPartyExperienceBroadcaster>();
builder.Services.AddSingleton<IPartyExperienceService, PartyExperienceService>();

// ADMIN SERVICES
builder.Services.AddScoped<ICharacterSearchService, CharacterSearchService>();
builder.Services.AddSingleton<IBanService, BanService>();
builder.Services.AddSingleton<IAdminService, AdminService>();

builder.Services.AddLeaderboards();

// AUTH
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>();
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Hs256Secret)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(10)
        };
    })
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationOptions.DefaultScheme, configureOptions: null);

builder.Services.AddAuthorization(opts =>
{
    // Calls that MUST come from game servers.
    opts.AddPolicy("ServerOnly", p => p
        .AddAuthenticationSchemes(ApiKeyAuthenticationOptions.DefaultScheme)
        .RequireAuthenticatedUser()
        .RequireRole("server"));
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseStaticFiles();

// GAME SERVER SOCKETS
app.UseWebSockets();
app.MapGameServerWebSockets();

_ = app.Services.GetRequiredService<WebSocketFriendNotifier>();
_ = app.Services.GetRequiredService<WebSocketPartyNotifier>();
_ = app.Services.GetRequiredService<PartyPresenceCleanupListener>();

app.MapControllers();

app.Run();

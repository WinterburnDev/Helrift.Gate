using Google.Apis.Auth.OAuth2;
using Helrift.Gate.Adapters.Firebase;
using Helrift.Gate.App;
using Microsoft.AspNetCore.Mvc;
using Polly;
using Polly.Extensions.Http;

// ----------------------
// Host + Services
// ----------------------
var builder = WebApplication.CreateBuilder(args);

// 1) Bind Firebase options for Adapters (separation of concerns preserved)
var fbOptions = new FirebaseOptions
{
    DatabaseUrl = builder.Configuration["Firebase:DatabaseUrl"]
        ?? throw new InvalidOperationException("Missing config: Firebase:DatabaseUrl"),
    Timeout = TimeSpan.FromSeconds(8)
};
builder.Services.AddSingleton(fbOptions);

// 2) Build Google service-account credential (Admin). You can supply a path in config
//    via Firebase:ServiceAccountJsonPath, or rely on GOOGLE_APPLICATION_CREDENTIALS env var.
var saPath = builder.Configuration["Firebase:ServiceAccountJsonPath"];
GoogleCredential credential = !string.IsNullOrWhiteSpace(saPath)
    ? GoogleCredential.FromFile(saPath)
    : GoogleCredential.GetApplicationDefault();

// Scope for Realtime Database admin access
credential = credential.CreateScoped(new[]
{
    "https://www.googleapis.com/auth/firebase.database",
    "https://www.googleapis.com/auth/userinfo.email"
});

builder.Services.AddSingleton(credential);

// 3) Auth handler that attaches/refreshes Bearer tokens (kept in a separate file)
builder.Services.AddTransient<GoogleAuthDelegatingHandler>();

// 4) Named HttpClient for Firebase RTDB (Admin OAuth)
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
builder.Services.AddControllers()
    .SetCompatibilityVersion(CompatibilityVersion.Version_3_0);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 6) Providers (use your REAL provider here)
builder.Services.AddScoped<IGameDataProvider, FirebaseGameDataProvider>();
// When you’re ready, also register Guild/Catalog/Merchant providers here.

// ----------------------
// Pipeline
// ----------------------
var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();

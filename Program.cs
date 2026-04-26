using KindleKeep.Api.API.Endpoints;
using KindleKeep.Api.API.Hubs;
using KindleKeep.Api.Core.Entities;
using KindleKeep.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddHttpClient("GitHub", client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("User-Agent", "KindleKeep-Auth-Agent");
});

builder.Services.AddHttpClient("WatcherClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "KindleKeep-Sentinel/1.0");
});

builder.Services.AddHttpClient("DiscordClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpClient("ResendClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Configure AOT-compatible JSON serialization
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddOpenApi();

// --- Infrastructure & Database Configuration ---

// Dynamically load the connection string from appsettings or environment variables
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Database connection string is missing.");

// 1. Create a Native AOT-friendly Npgsql DataSource
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
var dataSource = dataSourceBuilder.Build();

// 2. Register DbContext using DbContextPool to minimize memory allocation
builder.Services.AddDbContextPool<KindleDbContext>(options =>
{
    options.UseNpgsql(dataSource, npgsqlOptions =>
    {
        npgsqlOptions.CommandTimeout(15);
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null);
    });

    // TEMPORARILY COMMENT THIS OUT TO ALLOW COMPILATION
    options.UseModel(KindleKeep.Api.Infrastructure.Data.CompiledModels.KindleDbContextModel.Instance);
});

builder.Services.AddSingleton<KindleKeep.Api.Infrastructure.Identity.TokenService>();
builder.Services.AddSingleton<KindleKeep.Api.Infrastructure.Alerting.AlertManager>();
builder.Services.AddHostedService<KindleKeep.Api.Infrastructure.BackgroundServices.WatcherEngine>();

// --- Authentication, Authorization, & SignalR Configuration ---

// 1. JWT Authentication Configuration
var jwtKey = builder.Configuration["Jwt:Key"] 
    ?? throw new InvalidOperationException("JWT Secret Key is missing.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = "KindleKeep-Auth",
            ValidateAudience = true,
            ValidAudience = "KindleKeep-Dashboard",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        // Security logic for intercepting tokens on WebSocket connections
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                
                // If the request is for our SignalR hub and has a token in the query string
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/pulse"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// 2. SignalR Registration with AOT JSON Configuration
builder.Services.AddSignalR()
    .AddJsonProtocol(options => 
    {
        // Ensures SignalR uses our reflection-free JSON serializer context
        options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Enable Authentication and Authorization Middleware (Order is critical)
app.UseAuthentication();
app.UseAuthorization();

// Minimal health check endpoint utilizing a strongly-typed record
app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("Healthy", DateTime.UtcNow)))
   .WithName("GetHealthStatus");

// Register Endpoints and Hubs
app.MapAuthEndpoints();
app.MapHub<PulseHub>("/hubs/pulse");

app.Run();

// 1. Define the explicit record
public record HealthResponse(string Status, DateTime Timestamp);

// 2. Update the JSON context to explicitly register the new record
[JsonSerializable(typeof(User))]
[JsonSerializable(typeof(MonitorTarget))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(KindleKeep.Api.Core.DTOs.GithubTokenResponse))]
[JsonSerializable(typeof(KindleKeep.Api.Core.DTOs.GithubUserResponse))]
[JsonSerializable(typeof(KindleKeep.Api.Core.DTOs.AuthResponse))]
[JsonSerializable(typeof(KindleKeep.Api.Core.DTOs.PulseUpdate))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(KindleKeep.Api.Infrastructure.Alerting.DiscordPayload))]
[JsonSerializable(typeof(KindleKeep.Api.Infrastructure.Alerting.ResendPayload))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}
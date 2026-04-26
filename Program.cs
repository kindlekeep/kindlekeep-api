using System.Text.Json.Serialization;
using KindleKeep.Api.Core.Entities;
using KindleKeep.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddHttpClient("GitHub", client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("User-Agent", "KindleKeep-Auth-Agent");
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

    options.UseModel(KindleKeep.Api.Infrastructure.Data.CompiledModels.KindleDbContextModel.Instance);
});

builder.Services.AddSingleton<KindleKeep.Api.Infrastructure.Identity.TokenService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Minimal health check endpoint utilizing a strongly-typed record
app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("Healthy", DateTime.UtcNow)))
   .WithName("GetHealthStatus");

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
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}
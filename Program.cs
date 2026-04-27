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

var listeningUrl = builder.Configuration["WebHost:Url"] ?? "http://localhost:5247";
builder.WebHost.UseUrls(listeningUrl);

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

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        var origins = builder.Configuration["AllowedOrigins"]?.Split(',') ?? [];
        
        policy.WithOrigins(origins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Database connection string is missing.");

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
var dataSource = dataSourceBuilder.Build();

builder.Services.AddSingleton(dataSource);

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
builder.Services.AddSingleton<KindleKeep.Api.Infrastructure.Alerting.AlertManager>();
builder.Services.AddHostedService<KindleKeep.Api.Infrastructure.BackgroundServices.WatcherEngine>();
builder.Services.AddHostedService<KindleKeep.Api.Infrastructure.BackgroundServices.PruningService>();
builder.Services.AddExceptionHandler<KindleKeep.Api.Infrastructure.Exceptions.GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

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

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/pulse"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddSignalR()
    .AddJsonProtocol(options => 
    {
        options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
    });

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("FrontendPolicy");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => TypedResults.Ok("Healthy")).WithName("GetHealthStatus");

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapMonitorEndpoints();
app.MapHub<PulseHub>("/hubs/pulse");

app.Run();

[JsonSerializable(typeof(User))]
[JsonSerializable(typeof(MonitorTarget))]
[JsonSerializable(typeof(UptimeLog))]
[JsonSerializable(typeof(SecurityAudit))]
[JsonSerializable(typeof(AlertIncident))]
[JsonSerializable(typeof(KindleKeep.Api.Core.DTOs.GithubTokenResponse))]
[JsonSerializable(typeof(KindleKeep.Api.Core.DTOs.GithubUserResponse))]
[JsonSerializable(typeof(KindleKeep.Api.Core.DTOs.GoogleTokenResponse))]
[JsonSerializable(typeof(KindleKeep.Api.Core.DTOs.GoogleUserResponse))]
[JsonSerializable(typeof(KindleKeep.Api.Core.DTOs.GitlabTokenResponse))]
[JsonSerializable(typeof(KindleKeep.Api.Core.DTOs.GitlabUserResponse))]
[JsonSerializable(typeof(KindleKeep.Api.Core.DTOs.AuthResponse))]
[JsonSerializable(typeof(KindleKeep.Api.Core.DTOs.PulseUpdate))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(KindleKeep.Api.Infrastructure.Alerting.DiscordPayload))]
[JsonSerializable(typeof(KindleKeep.Api.Infrastructure.Alerting.ResendPayload))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
[JsonSerializable(typeof(KindleKeep.Api.Core.DTOs.UserProfileResponse))]
[JsonSerializable(typeof(KindleKeep.Api.Core.DTOs.CreateMonitorRequest))]
[JsonSerializable(typeof(KindleKeep.Api.Core.DTOs.MonitorResponse))]
[JsonSerializable(typeof(System.Collections.Generic.List<KindleKeep.Api.Core.DTOs.MonitorResponse>))]
[JsonSerializable(typeof(string))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Vibe.Api.Data;
using Vibe.Api.Endpoints;
using Vibe.Api.Infrastructure;
using Vibe.Api.Security;
using Vibe.Api.Services;

var builder = WebApplication.CreateBuilder(args);

/* --------------------------------------------------------------- services */

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<GoogleTokenValidator>();

builder.Services.AddDbContext<VibeDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("Vibe");
    if (string.IsNullOrWhiteSpace(connection) || connection.Contains("Data Source=vibe.dev.db", StringComparison.OrdinalIgnoreCase))
    {
        // Local development and the test run: a file-backed SQLite database that
        // needs no server. Azure SQL is used when a real connection string is set.
        options.UseSqlite(string.IsNullOrWhiteSpace(connection) ? "Data Source=vibe.dev.db" : connection);
    }
    else
    {
        options.UseSqlServer(connection);
    }
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration[JwtIssuer.IssuerKey] ?? "vibe-api",
            ValidAudience = builder.Configuration[JwtIssuer.AudienceKey] ?? "vibe-app",
            IssuerSigningKey = JwtIssuer.SigningKey(builder.Configuration),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddSingleton<IFcmSender, LoggingFcmSender>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<NotificationServiceStub>();
builder.Services.AddScoped<DecisionService>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddScoped<JwtIssuer>();
builder.Services.AddOpenApi();

var app = builder.Build();

/* ------------------------------------------------------------- pipeline */

app.UseVibeErrors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();

    if (string.IsNullOrWhiteSpace(app.Configuration[JwtIssuer.SigningKeyKey]))
    {
        // Never run a deployment on the development key.
        throw new InvalidOperationException(
            $"Set {JwtIssuer.SigningKeyKey} (32+ characters) before running outside Development.");
    }
}

app.UseAuthentication();
app.UseAuthorization();

/* --------------------------------------------------------------- schema */

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<VibeDbContext>();
    await db.Database.EnsureCreatedAsync();

    if (app.Environment.IsDevelopment())
    {
        await DevSeed.EnsureDemoAccountAsync(db, scope.ServiceProvider.GetRequiredService<TimeProvider>());
    }
}

/* -------------------------------------------------------------- endpoints */

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "vibe-api" })).WithTags("Diagnostics");

app.MapAuthEndpoints();
app.MapGroupEndpoints();
app.MapDecisionEndpoints();
app.MapSupportEndpoints();

app.Run();

/// <summary>Exposed so the xUnit project can boot the API with WebApplicationFactory.</summary>
public partial class Program;

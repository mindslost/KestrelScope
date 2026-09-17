using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KestrelScope;
using KestrelScope.Constants;
using KestrelScope.Services;
using KestrelScope.Tools;

if (args.Contains("--synthetic"))
{
    await SyntheticTelemetryGenerator.RunAsync(baseUrl: "http://localhost:5000", count: 5);
    return;
}

var builder = WebApplication.CreateBuilder(args);

string connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? AppConstants.Database.DefaultConnectionString;
builder.Configuration["ConnectionStrings:DefaultConnection"] = connectionString;

// Initialize SQLite schema and WAL mode
DbInitializer.Initialize(connectionString);

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddAuthentication(AppConstants.Auth.CookieScheme)
    .AddCookie(AppConstants.Auth.CookieScheme, options =>
    {
        options.Cookie.Name = AppConstants.Auth.CookieName;
        options.LoginPath = AppConstants.Auth.LoginPath;
        options.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddSingleton<AlertRulerWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AlertRulerWorker>());

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));

// Diagnostic webhook test sink
var receivedWebhooks = new ConcurrentQueue<JsonElement>();
app.MapPost("/api/test-webhook", async (HttpContext ctx) =>
{
    using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
    receivedWebhooks.Enqueue(doc.RootElement.Clone());
    return Results.Ok(new { status = "received" });
});

app.MapGet("/api/test-webhook", () => Results.Ok(receivedWebhooks.ToArray()));

// Trigger on-demand alert evaluation for testing/diagnostics
app.MapPost("/api/alerts/evaluate-now", async (AlertRulerWorker worker) =>
{
    int triggered = await worker.EvaluateAlertRulesAsync();
    return Results.Ok(new { status = "evaluated", newlyTriggered = triggered });
});

app.Run("http://0.0.0.0:5000");

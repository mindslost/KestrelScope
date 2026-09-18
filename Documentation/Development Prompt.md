```markdown
You are an expert .NET 8/9 Full-Stack Systems Architect and Senior Developer. Your objective is to build **KestrelScope**, a 100% sovereign, single-binary, self-hosted observability platform with zero third-party SaaS or external database dependencies.

Below are the complete design specifications, database schemas, API contracts, master execution tasklist, and C# starter scaffolds for the platform.

================================================================================
FILE 1: README.md
================================================================================
# KestrelScope — Self-Hosted .NET Observability Platform

## Project Overview
KestrelScope is a 100% sovereign, self-hosted, single-binary observability service built with .NET 8/9 and SQLite. It provides native OpenTelemetry (OTLP) metrics and trace ingestion, an embedded relational metastore, a background alert evaluation engine, and a web dashboard for monitoring microservice health.

## Architectural Mandates
1. Single-Binary Architecture: Runs as a single process containing web API endpoints, static UI assets (wwwroot), database engine, and background workers. No external container dependencies (no ClickHouse, PostgreSQL, or Redis) and no SaaS vendors.
2. Air-Gapped Operation: All static frontend assets (HTML, CSS, JS, Chart.js) are hosted locally within wwwroot/. No external CDN calls or internet access required.
3. High-Concurrency Persistence: Uses SQLite in Write-Ahead Logging (WAL) mode (`PRAGMA journal_mode=WAL;`) for concurrent read/write throughput without database lock contention.
4. Standard OTLP Receivers: Exposes standard OpenTelemetry ingestion endpoints at /v1/metrics and /v1/traces.

## Repository Directory Structure
├── Program.cs                      # Application entry point & service bootstrap
├── DbInitializer.cs                # SQLite schema initializer & WAL configuration
├── Controllers/
│   ├── OtlpIngestionController.cs  # Ingests OTLP metric and trace JSON payloads
│   └── ApiControllers.cs           # Web UI REST APIs (/api/auth, /api/metrics, /api/alerts)
├── Services/
│   └── AlertRulerWorker.cs         # BackgroundService evaluating metric alerts every 60s
├── Tools/
│   └── SyntheticTelemetryGenerator.cs # Test utility emitting sample OTLP data
└── wwwroot/                        # Embedded web UI static assets
    ├── index.html                  # Public landing / platform status
    ├── login.html                  # Auth page
    ├── dashboard.html              # Metrics explorer with Chart.js
    ├── traces.html                 # Distributed trace viewer
    ├── alerts.html                 # Alert rule assignment UI
    ├── css/main.css                # Dark-mode dashboard stylesheet
    └── js/
        ├── chart.min.js            # Locally hosted Chart.js library
        └── app.js                  # API client & UI event handlers

================================================================================
FILE 2: 01_backend_development_plan.md
================================================================================
# Backend Development Specification: KestrelScope

## 1. Architecture Overview
KestrelScope hosts web API endpoints, static UI files, OTLP ingestion receivers, an embedded SQLite database, and background alert evaluation inside a single ASP.NET Core process.

## 2. Database Schema & SQLite WAL Configuration
To ensure maximum read/write concurrency during high telemetry throughput, SQLite must be initialized with WAL mode:

PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
PRAGMA busy_timeout=5000;

### Schema Definitions
CREATE TABLE IF NOT EXISTS Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Username TEXT UNIQUE NOT NULL,
    PasswordHash TEXT NOT NULL,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS AlertRules (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    MetricName TEXT NOT NULL,
    Threshold REAL NOT NULL,
    WindowMinutes INTEGER NOT NULL,
    WebhookUrl TEXT NOT NULL,
    IsEnabled INTEGER DEFAULT 1
);

CREATE TABLE IF NOT EXISTS MetricSamples (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ServiceName TEXT NOT NULL,
    MetricName TEXT NOT NULL,
    Value REAL NOT NULL,
    Timestamp DATETIME NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_metrics_lookup 
ON MetricSamples(MetricName, ServiceName, Timestamp);

CREATE TABLE IF NOT EXISTS Traces (
    TraceId TEXT NOT NULL,
    SpanId TEXT PRIMARY KEY,
    ParentSpanId TEXT,
    ServiceName TEXT NOT NULL,
    SpanName TEXT NOT NULL,
    DurationMs REAL NOT NULL,
    StatusCode TEXT,
    Timestamp DATETIME NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_traces_timestamp ON Traces(Timestamp);

## 3. Ingestion Subsystem (/v1/metrics and /v1/traces)
The OtlpIngestionController handles HTTP POST payloads formatted in OTLP/JSON or Protobuf:
- Extracts resourceAttributes to identify service.name.
- Iterates over scopeMetrics and dataPoints (Gauges and Sums).
- Opens an explicit SQLite transaction and executes batch inserts.

## 4. Background Alert Evaluation (AlertRulerWorker)
The alert engine runs as an IHostedService on a 60-second loop:
1. Queries AlertRules where IsEnabled = 1.
2. For each rule, executes aggregate evaluation queries:
   SELECT ServiceName, AVG(Value) as AvgValue 
   FROM MetricSamples 
   WHERE MetricName = @metric AND Timestamp >= @windowStart 
   GROUP BY ServiceName 
   HAVING AvgValue > @threshold;
3. Dispatches JSON alert payloads to configured WebhookUrl via HttpClient.

================================================================================
FILE 3: 02_frontend_ui_specification.md
================================================================================
# Frontend & UI Specification: KestrelScope

## 1. Overview & wwwroot/ Architecture
The web UI is served directly from wwwroot/ using vanilla JavaScript, CSS, and a locally hosted Chart.js file (zero CDN dependencies).

## 2. REST API Contracts (/api/)
- POST /api/auth/login: Accepts { username, password }. Sets encrypted ObsSession cookie.
- POST /api/auth/logout: Clears session cookie.
- GET /api/auth/me: Returns { username, role }.
- GET /api/metrics/services: Returns array of unique ServiceName values.
- GET /api/metrics/names?service={name}: Returns metric names for the selected service.
- GET /api/metrics/series?service={s}&metric={m}&minutes={w}: Returns array [{ timestamp, value }].
- GET /api/metrics/stats: Returns { serviceCount, sampleCount24h, activeAlerts }.
- GET /api/alerts: Returns array of alert rules.
- POST /api/alerts: Creates/updates a rule { name, metricName, threshold, windowMinutes, webhookUrl }.
- PATCH /api/alerts/{id}/toggle: Toggles IsEnabled status.

================================================================================
FILE 4: 03_ai_agent_master_tasklist.md
================================================================================
# AI Coding Agent Master Tasklist

## Phase 1: Solution Setup & Database Initialization
- [ ] Create ASP.NET Core Web API project (.NET 8/9).
- [ ] Add NuGet packages: Microsoft.Data.Sqlite, Dapper.
- [ ] Implement DbInitializer.cs with PRAGMA journal_mode=WAL; and schema creation script.
- [ ] Verify database initialization creates observability.db on launch.

## Phase 2: OTLP Ingestion Controller
- [ ] Implement OtlpIngestionController.cs with routes /v1/metrics and /v1/traces.
- [ ] Add JSON parsing logic for OTLP resourceMetrics and service.name attributes.
- [ ] Implement SQLite transaction batching for fast sample insertion.
- [ ] Test ingestion endpoint using curl or Postman with OTLP payload.

## Phase 3: Background Alert Engine
- [ ] Create AlertRulerWorker.cs extending BackgroundService.
- [ ] Configure 60-second evaluation timer inside ExecuteAsync.
- [ ] Add SQL aggregate threshold calculation query over MetricSamples.
- [ ] Implement HttpClient webhook POST dispatching on alert threshold breaches.

## Phase 4: Authentication & Static Web Pipeline
- [ ] Enable static file middleware (app.UseStaticFiles()) mapping to wwwroot/.
- [ ] Implement Cookie Authentication middleware (CookieAuth).
- [ ] Build /api/auth/login, /api/auth/logout, and /api/auth/me API endpoints.
- [ ] Build /api/metrics/* and /api/alerts/* query REST endpoints.

## Phase 5: Web UI Development & Integration Testing
- [ ] Add local chart.min.js into wwwroot/js/chart.min.js.
- [ ] Implement dark-mode CSS in wwwroot/css/main.css.
- [ ] Create login.html, dashboard.html, and alerts.html.
- [ ] Implement JavaScript REST API calls in app.js to render live metric charts.
- [ ] Run end-to-end telemetry generator test and verify live chart rendering.

================================================================================
FILE 5: STARTER CODE SCAFFOLDS (05_sample_code/)
================================================================================

--- Program.cs ---
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

string connectionString = "Data Source=observability.db;";
builder.Configuration["ConnectionStrings:DefaultConnection"] = connectionString;

DbInitializer.Initialize(connectionString);

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddAuthentication("CookieAuth")
    .AddCookie("CookieAuth", options =>
    {
        options.Cookie.Name = "ObsSession";
        options.LoginPath = "/login.html";
    });

builder.Services.AddHostedService<AlertRulerWorker>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run("http://0.0.0.0:5000");


--- DbInitializer.cs ---
using Microsoft.Data.Sqlite;

public static class DbInitializer
{
    public static void Initialize(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using (var walCmd = connection.CreateCommand())
        {
            walCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            walCmd.ExecuteNonQuery();
        }

        var schemaSql = @"
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT UNIQUE NOT NULL,
                PasswordHash TEXT NOT NULL,
                CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS AlertRules (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                MetricName TEXT NOT NULL,
                Threshold REAL NOT NULL,
                WindowMinutes INTEGER NOT NULL,
                WebhookUrl TEXT NOT NULL,
                IsEnabled INTEGER DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS MetricSamples (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ServiceName TEXT NOT NULL,
                MetricName TEXT NOT NULL,
                Value REAL NOT NULL,
                Timestamp DATETIME NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_metrics_lookup 
            ON MetricSamples(MetricName, ServiceName, Timestamp);

            CREATE TABLE IF NOT EXISTS Traces (
                TraceId TEXT NOT NULL,
                SpanId TEXT PRIMARY KEY,
                ParentSpanId TEXT,
                ServiceName TEXT NOT NULL,
                SpanName TEXT NOT NULL,
                DurationMs REAL NOT NULL,
                StatusCode TEXT,
                Timestamp DATETIME NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_traces_timestamp ON Traces(Timestamp);
        ";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = schemaSql;
        cmd.ExecuteNonQuery();
    }
}


--- OtlpIngestionController.cs ---
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System;
using System.Text.Json;
using System.Threading.Tasks;

[ApiController]
[Route("v1")]
public class OtlpIngestionController : ControllerBase
{
    private readonly string _dbConn;

    public OtlpIngestionController(IConfiguration config)
    {
        _dbConn = config.GetConnectionString("DefaultConnection")!;
    }

    [HttpPost("metrics")]
    public async Task<IActionResult> IngestMetrics([FromBody] JsonElement payload)
    {
        if (!payload.TryGetProperty("resourceMetrics", out var resourceMetrics))
            return BadRequest("Invalid OTLP Payload");

        using var connection = new SqliteConnection(_dbConn);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        foreach (var rm in resourceMetrics.EnumerateArray())
        {
            string serviceName = ExtractServiceName(rm);

            if (!rm.TryGetProperty("scopeMetrics", out var scopeMetrics)) continue;
            foreach (var sm in scopeMetrics.EnumerateArray())
            {
                if (!sm.TryGetProperty("metrics", out var metrics)) continue;
                foreach (var metric in metrics.EnumerateArray())
                {
                    string metricName = metric.GetProperty("name").GetString()!;
                    
                    if (metric.TryGetProperty("sum", out var sum) && sum.TryGetProperty("dataPoints", out var dataPoints))
                    {
                        foreach (var dp in dataPoints.EnumerateArray())
                        {
                            double val = dp.TryGetProperty("asDouble", out var d) ? d.GetDouble() : dp.GetProperty("asInt").GetInt64();
                            
                            using var cmd = connection.CreateCommand();
                            cmd.Transaction = transaction;
                            cmd.CommandText = @"
                                INSERT INTO MetricSamples (ServiceName, MetricName, Value, Timestamp) 
                                VALUES (@service, @metric, @val, @time);";
                            cmd.Parameters.AddWithValue("@service", serviceName);
                            cmd.Parameters.AddWithValue("@metric", metricName);
                            cmd.Parameters.AddWithValue("@val", val);
                            cmd.Parameters.AddWithValue("@time", DateTime.UtcNow);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }
            }
        }

        await transaction.CommitAsync();
        return Ok();
    }

    private static string ExtractServiceName(JsonElement resourceMetric)
    {
        if (resourceMetric.TryGetProperty("resource", out var res) &&
            res.TryGetProperty("attributes", out var attrs))
        {
            foreach (var attr in attrs.EnumerateArray())
            {
                if (attr.GetProperty("key").GetString() == "service.name")
                    return attr.GetProperty("value").GetProperty("stringValue").GetString() ?? "unknown-service";
            }
        }
        return "unknown-service";
    }
}


--- AlertRulerWorker.cs ---
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public class AlertRulerWorker : BackgroundService
{
    private readonly string _dbConn;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AlertRulerWorker> _logger;

    public AlertRulerWorker(IConfiguration config, HttpClient httpClient, ILogger<AlertRulerWorker> logger)
    {
        _dbConn = config.GetConnectionString("DefaultConnection")!;
        _httpClient = httpClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateAlertRulesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating alert rules.");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    private async Task EvaluateAlertRulesAsync()
    {
        using var connection = new SqliteConnection(_dbConn);
        await connection.OpenAsync();

        var rules = new List<(int Id, string Name, string Metric, double Threshold, int Window, string Webhook)>();
        using (var ruleCmd = connection.CreateCommand())
        {
            ruleCmd.CommandText = "SELECT Id, Name, MetricName, Threshold, WindowMinutes, WebhookUrl FROM AlertRules WHERE IsEnabled = 1;";
            using var reader = await ruleCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rules.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetDouble(3), reader.GetInt32(4), reader.GetString(5)));
            }
        }

        foreach (var rule in rules)
        {
            using var evalCmd = connection.CreateCommand();
            evalCmd.CommandText = @"
                SELECT ServiceName, AVG(Value) as AvgValue 
                FROM MetricSamples 
                WHERE MetricName = @metric AND Timestamp >= @windowStart 
                GROUP BY ServiceName 
                HAVING AvgValue > @threshold;";

            evalCmd.Parameters.AddWithValue("@metric", rule.Metric);
            evalCmd.Parameters.AddWithValue("@windowStart", DateTime.UtcNow.AddMinutes(-rule.Window));
            evalCmd.Parameters.AddWithValue("@threshold", rule.Threshold);

            using var evalReader = await evalCmd.ExecuteReaderAsync();
            while (await evalReader.ReadAsync())
            {
                string service = evalReader.GetString(0);
                double avgValue = evalReader.GetDouble(1);

                _logger.LogWarning("ALERT TRIGGERED: Rule '{Rule}' breached by {Service}. Value: {Val}", rule.Name, service, avgValue);

                var payload = JsonSerializer.Serialize(new {
                    alert = rule.Name,
                    service = service,
                    metric = rule.Metric,
                    currentValue = avgValue,
                    threshold = rule.Threshold,
                    triggeredAt = DateTime.UtcNow
                });

                await _httpClient.PostAsync(rule.Webhook, new StringContent(payload, Encoding.UTF8, "application/json"));
            }
        }
    }
}


--- SyntheticTelemetryGenerator.cs ---
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class SyntheticTelemetryGenerator
{
    public static async Task Main(string[] args)
    {
        using var client = new HttpClient();
        var rand = new Random();
        string endpoint = "http://localhost:5000/v1/metrics";

        Console.WriteLine("Starting Synthetic Telemetry Generator. Emitting metrics every 2 seconds...");

        while (true)
        {
            double latency = 100 + rand.NextDouble() * 400;
            
            var payload = new
            {
                resourceMetrics = new[]
                {
                    new
                    {
                        resource = new
                        {
                            attributes = new[]
                            {
                                new { key = "service.name", value = new { stringValue = "order-processor-service" } }
                            }
                        },
                        scopeMetrics = new[]
                        {
                            new
                            {
                                metrics = new[]
                                {
                                    new
                                    {
                                        name = "http.server.duration",
                                        sum = new
                                        {
                                            dataPoints = new[]
                                            {
                                                new { asDouble = latency }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            string json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await client.PostAsync(endpoint, content);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sent Metric Sample: {latency:F2}ms | Status: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending metric: {ex.Message}");
            }

            await Task.Delay(2000);
        }
    }
}

================================================================================
INSTRUCTIONS FOR GEMINI 3.8 FLASH
================================================================================
Read all specifications above. Please start by implementing Phase 1 of `03_ai_agent_master_tasklist.md`:
1. Initialize the ASP.NET Core solution structure for KestrelScope.
2. Implement `DbInitializer.cs` with `PRAGMA journal_mode=WAL;` and schema creation scripts.
3. Configure `Program.cs` to bootstrap the database and set up middleware.
4. Provide fully compilable, production-ready C# code for Phase 1.
```

***

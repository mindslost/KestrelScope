using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Configuration
string serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "order-service";
string otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:5000";

builder.Services.AddHttpClient();
builder.Services.AddSingleton(new TelemetryDispatcher(serviceName, otlpEndpoint));
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelemetryDispatcher>());

var app = builder.Build();

var dispatcher = app.Services.GetRequiredService<TelemetryDispatcher>();

// Diagnostic Health Check
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = serviceName, otlpTarget = otlpEndpoint }));

// 1. Create Order: Simulates full multi-span distributed execution
app.MapPost("/api/orders", async (OrderRequest req) =>
{
    var sw = Stopwatch.StartNew();
    string traceId = Guid.NewGuid().ToString("N");
    string rootSpanId = Guid.NewGuid().ToString("N")[..16];
    long startNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000;

    // Simulate Step 1: Inventory Validation
    string invSpanId = Guid.NewGuid().ToString("N")[..16];
    long invStart = startNano + 5_000_000;
    await Task.Delay(15);
    long invEnd = invStart + 15_000_000;
    dispatcher.RecordSpan(traceId, invSpanId, rootSpanId, "InventoryService.ValidateStock", invStart, invEnd, 1);

    // Simulate Step 2: Payment Processing
    string paySpanId = Guid.NewGuid().ToString("N")[..16];
    long payStart = invEnd + 5_000_000;
    await Task.Delay(40);
    long payEnd = payStart + 40_000_000;
    dispatcher.RecordSpan(traceId, paySpanId, rootSpanId, "PaymentGateway.ChargeCard", payStart, payEnd, 1);

    // Simulate Step 3: Database Persistence
    string dbSpanId = Guid.NewGuid().ToString("N")[..16];
    long dbStart = payEnd + 5_000_000;
    await Task.Delay(20);
    long dbEnd = dbStart + 20_000_000;
    dispatcher.RecordSpan(traceId, dbSpanId, rootSpanId, "OrderRepository.SaveOrder", dbStart, dbEnd, 1);

    sw.Stop();
    long endNano = dbEnd + 5_000_000;
    dispatcher.RecordSpan(traceId, rootSpanId, null, "POST /api/orders", startNano, endNano, 1);

    // Record Duration Metric
    dispatcher.RecordMetric("http.server.request.duration", sw.Elapsed.TotalMilliseconds);
    dispatcher.RecordMetric("orders.created.count", 1.0);

    var orderId = "ORD-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
    return Results.Created($"/api/orders/{orderId}", new
    {
        orderId,
        customerId = req.CustomerId,
        totalAmount = req.TotalAmount,
        status = "Confirmed",
        durationMs = sw.Elapsed.TotalMilliseconds
    });
});

// 2. Get Order by Id
app.MapGet("/api/orders/{id}", async (string id) =>
{
    var sw = Stopwatch.StartNew();
    string traceId = Guid.NewGuid().ToString("N");
    string rootSpanId = Guid.NewGuid().ToString("N")[..16];
    long startNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000;

    // Simulate repository query
    string dbSpanId = Guid.NewGuid().ToString("N")[..16];
    long dbStart = startNano + 5_000_000;
    await Task.Delay(10);
    long dbEnd = dbStart + 10_000_000;
    dispatcher.RecordSpan(traceId, dbSpanId, rootSpanId, "OrderRepository.FindById", dbStart, dbEnd, 1);

    sw.Stop();
    long endNano = dbEnd + 2_000_000;
    dispatcher.RecordSpan(traceId, rootSpanId, null, $"GET /api/orders/{id}", startNano, endNano, 1);

    dispatcher.RecordMetric("http.server.request.duration", sw.Elapsed.TotalMilliseconds);

    return Results.Ok(new
    {
        orderId = id,
        status = "Confirmed",
        durationMs = sw.Elapsed.TotalMilliseconds
    });
});

// 3. Failed Order: Simulates critical breach & error status span
app.MapPost("/api/orders/fail", async () =>
{
    var sw = Stopwatch.StartNew();
    string traceId = Guid.NewGuid().ToString("N");
    string rootSpanId = Guid.NewGuid().ToString("N")[..16];
    long startNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000;

    // Simulate failing payment gateway
    string paySpanId = Guid.NewGuid().ToString("N")[..16];
    long payStart = startNano + 10_000_000;
    await Task.Delay(120);
    long payEnd = payStart + 120_000_000;
    dispatcher.RecordSpan(traceId, paySpanId, rootSpanId, "PaymentGateway.ChargeCard", payStart, payEnd, 2); // 2 = Error

    sw.Stop();
    // High simulated latency to breach thresholds (e.g. 550ms)
    long endNano = startNano + 550_000_000;
    dispatcher.RecordSpan(traceId, rootSpanId, null, "POST /api/orders/fail", startNano, endNano, 2);

    dispatcher.RecordMetric("http.server.request.duration", 550.0);
    dispatcher.RecordMetric("orders.failed.count", 1.0);

    return Results.Problem(
        detail: "Payment declined: Card issuer rejected transaction.",
        statusCode: StatusCodes.Status500InternalServerError,
        title: "Order Processing Failure"
    );
});

// 4. Force Telemetry Dispatch Flush
app.MapPost("/api/telemetry/flush", async () =>
{
    await dispatcher.FlushAsync();
    return Results.Ok(new { status = "flushed" });
});

string bindUrl = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080";
app.Run(bindUrl);

// ============================================================================
// Telemetry Dispatcher Background Service
// ============================================================================
public class TelemetryDispatcher : BackgroundService
{
    private readonly string _serviceName;
    private readonly string _otlpBaseUrl;
    private readonly HttpClient _http = new();
    private readonly ConcurrentQueue<SpanRecord> _spans = new();
    private readonly ConcurrentQueue<MetricRecord> _metrics = new();

    public record SpanRecord(string TraceId, string SpanId, string? ParentSpanId, string Name, long StartNano, long EndNano, int StatusCode);
    public record MetricRecord(string MetricName, double Value, long TimeNano);

    public TelemetryDispatcher(string serviceName, string otlpBaseUrl)
    {
        _serviceName = serviceName;
        _otlpBaseUrl = otlpBaseUrl.TrimEnd('/');
    }

    public void RecordSpan(string traceId, string spanId, string? parentSpanId, string name, long startNano, long endNano, int statusCode)
    {
        _spans.Enqueue(new SpanRecord(traceId, spanId, parentSpanId, name, startNano, endNano, statusCode));
    }

    public void RecordMetric(string metricName, double value)
    {
        long nowNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000;
        _metrics.Enqueue(new MetricRecord(metricName, value, nowNano));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FlushAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TelemetryDispatcher] Error flushing telemetry: {ex.Message}");
            }

            await Task.Delay(1000, stoppingToken);
        }
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        // 1. Flush Spans
        var pendingSpans = new List<SpanRecord>();
        while (_spans.TryDequeue(out var s)) pendingSpans.Add(s);

        if (pendingSpans.Count > 0)
        {
            var spanObjects = pendingSpans.Select(s => new
            {
                traceId = s.TraceId,
                spanId = s.SpanId,
                parentSpanId = s.ParentSpanId,
                name = s.Name,
                startTimeUnixNano = s.StartNano.ToString(),
                endTimeUnixNano = s.EndNano.ToString(),
                status = new { code = s.StatusCode }
            }).ToArray();

            var tracePayload = new
            {
                resourceSpans = new object[]
                {
                    new
                    {
                        resource = new
                        {
                            attributes = new[]
                            {
                                new { key = "service.name", value = new { stringValue = _serviceName } }
                            }
                        },
                        scopeSpans = new[]
                        {
                            new
                            {
                                spans = spanObjects
                            }
                        }
                    }
                }
            };

            string json = JsonSerializer.Serialize(tracePayload);
            var res = await _http.PostAsync($"{_otlpBaseUrl}/v1/traces", new StringContent(json, Encoding.UTF8, "application/json"), ct);
            Console.WriteLine($"[TelemetryDispatcher] Emitted {pendingSpans.Count} spans to {_otlpBaseUrl}/v1/traces -> {res.StatusCode}");
        }

        // 2. Flush Metrics
        var pendingMetrics = new List<MetricRecord>();
        while (_metrics.TryDequeue(out var m)) pendingMetrics.Add(m);

        if (pendingMetrics.Count > 0)
        {
            var metricGroups = pendingMetrics.GroupBy(m => m.MetricName);
            var metricObjects = new List<object>();

            foreach (var group in metricGroups)
            {
                var dps = group.Select(m => new
                {
                    asDouble = m.Value,
                    timeUnixNano = m.TimeNano.ToString()
                }).ToArray();

                metricObjects.Add(new
                {
                    name = group.Key,
                    sum = new
                    {
                        dataPoints = dps
                    }
                });
            }

            var metricPayload = new
            {
                resourceMetrics = new object[]
                {
                    new
                    {
                        resource = new
                        {
                            attributes = new[]
                            {
                                new { key = "service.name", value = new { stringValue = _serviceName } }
                            }
                        },
                        scopeMetrics = new[]
                        {
                            new
                            {
                                metrics = metricObjects.ToArray()
                            }
                        }
                    }
                }
            };

            string json = JsonSerializer.Serialize(metricPayload);
            var res = await _http.PostAsync($"{_otlpBaseUrl}/v1/metrics", new StringContent(json, Encoding.UTF8, "application/json"), ct);
            Console.WriteLine($"[TelemetryDispatcher] Emitted {pendingMetrics.Count} metric samples to {_otlpBaseUrl}/v1/metrics -> {res.StatusCode}");
        }
    }
}

public record OrderRequest(string CustomerId, decimal TotalAmount);

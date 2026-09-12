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

if (string.Equals(Environment.GetEnvironmentVariable("ENABLE_SIMULATED_TRAFFIC"), "true", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHostedService<SimulatedTrafficWorker>();
}

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

    var orderId = "ORD-" + Guid.NewGuid().ToString("N")[..8].ToUpper();

    // Correlated Structured Logs
    dispatcher.RecordLog(traceId, invSpanId, "INFO", 9, $"Validating stock for customer {req.CustomerId}");
    dispatcher.RecordLog(traceId, paySpanId, "INFO", 9, $"Payment card authorization approved for ${req.TotalAmount:F2}");
    dispatcher.RecordLog(traceId, dbSpanId, "INFO", 9, $"Order {orderId} successfully persisted to database");

    // Record Duration Metric
    dispatcher.RecordMetric("http.server.request.duration", sw.Elapsed.TotalMilliseconds);
    dispatcher.RecordMetric("orders.created.count", 1.0);

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

    // Correlated Warning and Error Logs
    dispatcher.RecordLog(traceId, paySpanId, "WARN", 13, "Payment processing latency exceeding 100ms threshold");
    dispatcher.RecordLog(traceId, rootSpanId, "ERROR", 17, "Payment declined: Card issuer rejected transaction. Order aborted.");

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

#region Telemetry Dispatcher Background Service
public class TelemetryDispatcher : BackgroundService
{
    private readonly string _serviceName;
    private readonly string _otlpBaseUrl;
    private readonly HttpClient _http = new();
    private readonly ConcurrentQueue<SpanRecord> _spans = new();
    private readonly ConcurrentQueue<MetricRecord> _metrics = new();
    private readonly ConcurrentQueue<LogMessageRecord> _logs = new();

    public record SpanRecord(string TraceId, string SpanId, string? ParentSpanId, string Name, long StartNano, long EndNano, int StatusCode);
    public record MetricRecord(string MetricName, double Value, long TimeNano);
    public record LogMessageRecord(string? TraceId, string? SpanId, string SeverityText, int SeverityNumber, string Body, long TimeNano, Dictionary<string, string>? Attributes);

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

    public void RecordLog(string? traceId, string? spanId, string severityText, int severityNumber, string body, Dictionary<string, string>? attributes = null)
    {
        long nowNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000;
        _logs.Enqueue(new LogMessageRecord(traceId, spanId, severityText, severityNumber, body, nowNano, attributes));
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

        // 3. Flush Logs
        var pendingLogs = new List<LogMessageRecord>();
        while (_logs.TryDequeue(out var l)) pendingLogs.Add(l);

        if (pendingLogs.Count > 0)
        {
            var logRecords = pendingLogs.Select(l =>
            {
                var attrs = l.Attributes != null && l.Attributes.Count > 0
                    ? l.Attributes.Select(kv => new { key = kv.Key, value = new { stringValue = kv.Value } }).ToArray()
                    : Array.Empty<object>();

                return new
                {
                    timeUnixNano = l.TimeNano.ToString(),
                    observedTimeUnixNano = l.TimeNano.ToString(),
                    severityText = l.SeverityText,
                    severityNumber = l.SeverityNumber,
                    body = new { stringValue = l.Body },
                    traceId = l.TraceId,
                    spanId = l.SpanId,
                    attributes = attrs
                };
            }).ToArray();

            var logPayload = new
            {
                resourceLogs = new object[]
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
                        scopeLogs = new[]
                        {
                            new
                            {
                                logRecords = logRecords
                            }
                        }
                    }
                }
            };

            string json = JsonSerializer.Serialize(logPayload);
            var res = await _http.PostAsync($"{_otlpBaseUrl}/v1/logs", new StringContent(json, Encoding.UTF8, "application/json"), ct);
            Console.WriteLine($"[TelemetryDispatcher] Emitted {pendingLogs.Count} log records to {_otlpBaseUrl}/v1/logs -> {res.StatusCode}");
        }
    }
}
#endregion

#region Simulated Traffic Background Worker
public class SimulatedTrafficWorker : BackgroundService
{
    private readonly TelemetryDispatcher _dispatcher;
    private readonly Random _rand = new();

    public SimulatedTrafficWorker(TelemetryDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(2000, stoppingToken);
        int orderSeq = 100;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                orderSeq++;
                bool isError = _rand.Next(0, 12) == 0;
                string traceId = Guid.NewGuid().ToString("N");
                string rootSpanId = Guid.NewGuid().ToString("N")[..16];
                long startNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000;
                string orderId = $"ORD-{orderSeq}";

                if (!isError)
                {
                    double duration = 40 + _rand.NextDouble() * 85;
                    string invSpanId = Guid.NewGuid().ToString("N")[..16];
                    string paySpanId = Guid.NewGuid().ToString("N")[..16];
                    string dbSpanId = Guid.NewGuid().ToString("N")[..16];

                    long invStart = startNano + 2_000_000;
                    long invEnd = invStart + 15_000_000;
                    _dispatcher.RecordSpan(traceId, invSpanId, rootSpanId, "InventoryService.ValidateStock", invStart, invEnd, 1);

                    long payStart = invEnd + 3_000_000;
                    long payEnd = payStart + 35_000_000;
                    _dispatcher.RecordSpan(traceId, paySpanId, rootSpanId, "PaymentGateway.ChargeCard", payStart, payEnd, 1);

                    long dbStart = payEnd + 2_000_000;
                    long dbEnd = dbStart + 20_000_000;
                    _dispatcher.RecordSpan(traceId, dbSpanId, rootSpanId, "OrderRepository.SaveOrder", dbStart, dbEnd, 1);

                    long endNano = dbEnd + 2_000_000;
                    _dispatcher.RecordSpan(traceId, rootSpanId, null, "POST /api/orders", startNano, endNano, 1);

                    _dispatcher.RecordLog(traceId, invSpanId, "INFO", 9, $"Inventory reservation confirmed for order {orderId}");
                    _dispatcher.RecordLog(traceId, paySpanId, "INFO", 9, $"Card payment authorized for ${25 + _rand.NextDouble() * 150:F2}");
                    _dispatcher.RecordLog(traceId, dbSpanId, "INFO", 9, $"Order {orderId} committed to database");

                    _dispatcher.RecordMetric("http.server.request.duration", duration);
                    _dispatcher.RecordMetric("orders.created.count", 1.0);
                }
                else
                {
                    double breachDuration = 350 + _rand.NextDouble() * 200;
                    string paySpanId = Guid.NewGuid().ToString("N")[..16];
                    long payStart = startNano + 5_000_000;
                    long payEnd = startNano + 320_000_000;
                    _dispatcher.RecordSpan(traceId, paySpanId, rootSpanId, "PaymentGateway.ChargeCard", payStart, payEnd, 2);

                    long endNano = startNano + (long)(breachDuration * 1_000_000);
                    _dispatcher.RecordSpan(traceId, rootSpanId, null, "POST /api/orders/fail", startNano, endNano, 2);

                    _dispatcher.RecordLog(traceId, paySpanId, "WARN", 13, $"Payment gateway timeout on transaction {orderId} ({breachDuration:F1}ms)");
                    _dispatcher.RecordLog(traceId, rootSpanId, "ERROR", 17, $"Order {orderId} failed: upstream payment provider rejected transaction");

                    _dispatcher.RecordMetric("http.server.request.duration", breachDuration);
                    _dispatcher.RecordMetric("orders.failed.count", 1.0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SimulatedTrafficWorker] Warning: {ex.Message}");
            }

            await Task.Delay(_rand.Next(2500, 4000), stoppingToken);
        }
    }
}
#endregion

public record OrderRequest(string CustomerId, decimal TotalAmount);

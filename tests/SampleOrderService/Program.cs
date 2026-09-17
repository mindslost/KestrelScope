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
string serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? ServiceConstants.DefaultServiceName;
string otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? ServiceConstants.DefaultOtlpEndpoint;

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
    long startNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * ServiceConstants.NanoPerMilli;

    // Simulate Step 1: Inventory Validation
    string invSpanId = Guid.NewGuid().ToString("N")[..16];
    long invStart = startNano + 5 * ServiceConstants.NanoPerMilli;
    await Task.Delay(15);
    long invEnd = invStart + 15 * ServiceConstants.NanoPerMilli;
    dispatcher.RecordSpan(traceId, invSpanId, rootSpanId, ServiceConstants.Spans.ValidateStock, invStart, invEnd, ServiceConstants.StatusCodes.Ok);

    // Simulate Step 2: Payment Processing
    string paySpanId = Guid.NewGuid().ToString("N")[..16];
    long payStart = invEnd + 5 * ServiceConstants.NanoPerMilli;
    await Task.Delay(40);
    long payEnd = payStart + 40 * ServiceConstants.NanoPerMilli;
    dispatcher.RecordSpan(traceId, paySpanId, rootSpanId, ServiceConstants.Spans.ChargeCard, payStart, payEnd, ServiceConstants.StatusCodes.Ok);

    // Simulate Step 3: Database Persistence
    string dbSpanId = Guid.NewGuid().ToString("N")[..16];
    long dbStart = payEnd + 5 * ServiceConstants.NanoPerMilli;
    await Task.Delay(20);
    long dbEnd = dbStart + 20 * ServiceConstants.NanoPerMilli;
    dispatcher.RecordSpan(traceId, dbSpanId, rootSpanId, ServiceConstants.Spans.SaveOrder, dbStart, dbEnd, ServiceConstants.StatusCodes.Ok);

    sw.Stop();
    long endNano = dbEnd + 5 * ServiceConstants.NanoPerMilli;
    dispatcher.RecordSpan(traceId, rootSpanId, null, ServiceConstants.Spans.CreateOrder, startNano, endNano, ServiceConstants.StatusCodes.Ok);

    var orderId = "ORD-" + Guid.NewGuid().ToString("N")[..8].ToUpper();

    // Correlated Structured Logs
    dispatcher.RecordLog(traceId, invSpanId, ServiceConstants.Severity.Info, ServiceConstants.Severity.InfoNumber, $"Validating stock for customer {req.CustomerId}");
    dispatcher.RecordLog(traceId, paySpanId, ServiceConstants.Severity.Info, ServiceConstants.Severity.InfoNumber, $"Payment card authorization approved for ${req.TotalAmount:F2}");
    dispatcher.RecordLog(traceId, dbSpanId, ServiceConstants.Severity.Info, ServiceConstants.Severity.InfoNumber, $"Order {orderId} successfully persisted to database");

    // Record Duration Metric
    dispatcher.RecordMetric(ServiceConstants.Metrics.HttpServerRequestDuration, sw.Elapsed.TotalMilliseconds);
    dispatcher.RecordMetric(ServiceConstants.Metrics.OrdersCreatedCount, 1.0);

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
    long startNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * ServiceConstants.NanoPerMilli;

    // Simulate repository query
    string dbSpanId = Guid.NewGuid().ToString("N")[..16];
    long dbStart = startNano + 5 * ServiceConstants.NanoPerMilli;
    await Task.Delay(10);
    long dbEnd = dbStart + 10 * ServiceConstants.NanoPerMilli;
    dispatcher.RecordSpan(traceId, dbSpanId, rootSpanId, ServiceConstants.Spans.FindOrderById, dbStart, dbEnd, ServiceConstants.StatusCodes.Ok);

    sw.Stop();
    long endNano = dbEnd + 2 * ServiceConstants.NanoPerMilli;
    dispatcher.RecordSpan(traceId, rootSpanId, null, $"GET /api/orders/{id}", startNano, endNano, ServiceConstants.StatusCodes.Ok);

    dispatcher.RecordMetric(ServiceConstants.Metrics.HttpServerRequestDuration, sw.Elapsed.TotalMilliseconds);

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
    long startNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * ServiceConstants.NanoPerMilli;

    // Simulate failing payment gateway
    string paySpanId = Guid.NewGuid().ToString("N")[..16];
    long payStart = startNano + 10 * ServiceConstants.NanoPerMilli;
    await Task.Delay(120);
    long payEnd = payStart + 120 * ServiceConstants.NanoPerMilli;
    dispatcher.RecordSpan(traceId, paySpanId, rootSpanId, ServiceConstants.Spans.ChargeCard, payStart, payEnd, ServiceConstants.StatusCodes.Error);

    sw.Stop();
    // High simulated latency to breach thresholds (e.g. 550ms)
    long endNano = startNano + 550 * ServiceConstants.NanoPerMilli;
    dispatcher.RecordSpan(traceId, rootSpanId, null, ServiceConstants.Spans.FailOrder, startNano, endNano, ServiceConstants.StatusCodes.Error);

    dispatcher.RecordMetric(ServiceConstants.Metrics.HttpServerRequestDuration, 550.0);
    dispatcher.RecordMetric(ServiceConstants.Metrics.OrdersFailedCount, 1.0);

    // Correlated Warning and Error Logs
    dispatcher.RecordLog(traceId, paySpanId, ServiceConstants.Severity.Warn, ServiceConstants.Severity.WarnNumber, "Payment processing latency exceeding 100ms threshold");
    dispatcher.RecordLog(traceId, rootSpanId, ServiceConstants.Severity.Error, ServiceConstants.Severity.ErrorNumber, "Payment declined: Card issuer rejected transaction. Order aborted.");

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

string bindUrl = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? ServiceConstants.DefaultBindUrl;
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
                long startNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * ServiceConstants.NanoPerMilli;
                string orderId = $"ORD-{orderSeq}";

                if (!isError)
                {
                    double duration = 40 + _rand.NextDouble() * 85;
                    string invSpanId = Guid.NewGuid().ToString("N")[..16];
                    string paySpanId = Guid.NewGuid().ToString("N")[..16];
                    string dbSpanId = Guid.NewGuid().ToString("N")[..16];

                    long invStart = startNano + 2 * ServiceConstants.NanoPerMilli;
                    long invEnd = invStart + 15 * ServiceConstants.NanoPerMilli;
                    _dispatcher.RecordSpan(traceId, invSpanId, rootSpanId, ServiceConstants.Spans.ValidateStock, invStart, invEnd, ServiceConstants.StatusCodes.Ok);

                    long payStart = invEnd + 3 * ServiceConstants.NanoPerMilli;
                    long payEnd = payStart + 35 * ServiceConstants.NanoPerMilli;
                    _dispatcher.RecordSpan(traceId, paySpanId, rootSpanId, ServiceConstants.Spans.ChargeCard, payStart, payEnd, ServiceConstants.StatusCodes.Ok);

                    long dbStart = payEnd + 2 * ServiceConstants.NanoPerMilli;
                    long dbEnd = dbStart + 20 * ServiceConstants.NanoPerMilli;
                    _dispatcher.RecordSpan(traceId, dbSpanId, rootSpanId, ServiceConstants.Spans.SaveOrder, dbStart, dbEnd, ServiceConstants.StatusCodes.Ok);

                    long endNano = dbEnd + 2 * ServiceConstants.NanoPerMilli;
                    _dispatcher.RecordSpan(traceId, rootSpanId, null, ServiceConstants.Spans.CreateOrder, startNano, endNano, ServiceConstants.StatusCodes.Ok);

                    _dispatcher.RecordLog(traceId, invSpanId, ServiceConstants.Severity.Info, ServiceConstants.Severity.InfoNumber, $"Inventory reservation confirmed for order {orderId}");
                    _dispatcher.RecordLog(traceId, paySpanId, ServiceConstants.Severity.Info, ServiceConstants.Severity.InfoNumber, $"Card payment authorized for ${25 + _rand.NextDouble() * 150:F2}");
                    _dispatcher.RecordLog(traceId, dbSpanId, ServiceConstants.Severity.Info, ServiceConstants.Severity.InfoNumber, $"Order {orderId} committed to database");

                    _dispatcher.RecordMetric(ServiceConstants.Metrics.HttpServerRequestDuration, duration);
                    _dispatcher.RecordMetric(ServiceConstants.Metrics.OrdersCreatedCount, 1.0);
                }
                else
                {
                    double breachDuration = 350 + _rand.NextDouble() * 200;
                    string paySpanId = Guid.NewGuid().ToString("N")[..16];
                    long payStart = startNano + 5 * ServiceConstants.NanoPerMilli;
                    long payEnd = startNano + 320 * ServiceConstants.NanoPerMilli;
                    _dispatcher.RecordSpan(traceId, paySpanId, rootSpanId, ServiceConstants.Spans.ChargeCard, payStart, payEnd, ServiceConstants.StatusCodes.Error);

                    long endNano = startNano + (long)(breachDuration * ServiceConstants.NanoPerMilli);
                    _dispatcher.RecordSpan(traceId, rootSpanId, null, ServiceConstants.Spans.FailOrder, startNano, endNano, ServiceConstants.StatusCodes.Error);

                    _dispatcher.RecordLog(traceId, paySpanId, ServiceConstants.Severity.Warn, ServiceConstants.Severity.WarnNumber, $"Payment gateway timeout on transaction {orderId} ({breachDuration:F1}ms)");
                    _dispatcher.RecordLog(traceId, rootSpanId, ServiceConstants.Severity.Error, ServiceConstants.Severity.ErrorNumber, $"Order {orderId} failed: upstream payment provider rejected transaction");

                    _dispatcher.RecordMetric(ServiceConstants.Metrics.HttpServerRequestDuration, breachDuration);
                    _dispatcher.RecordMetric(ServiceConstants.Metrics.OrdersFailedCount, 1.0);
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

public static class ServiceConstants
{
    public const string DefaultServiceName = "order-service";
    public const string DefaultOtlpEndpoint = "http://localhost:5000";
    public const string DefaultBindUrl = "http://0.0.0.0:8080";
    public const long NanoPerMilli = 1_000_000L;

    public static class Metrics
    {
        public const string HttpServerRequestDuration = "http.server.request.duration";
        public const string OrdersCreatedCount = "orders.created.count";
        public const string OrdersFailedCount = "orders.failed.count";
    }

    public static class Spans
    {
        public const string CreateOrder = "POST /api/orders";
        public const string ValidateStock = "InventoryService.ValidateStock";
        public const string ChargeCard = "PaymentGateway.ChargeCard";
        public const string SaveOrder = "OrderRepository.SaveOrder";
        public const string FindOrderById = "OrderRepository.FindById";
        public const string FailOrder = "POST /api/orders/fail";
    }

    public static class StatusCodes
    {
        public const int Ok = 1;
        public const int Error = 2;
    }

    public static class Severity
    {
        public const string Info = "INFO";
        public const int InfoNumber = 9;
        public const string Warn = "WARN";
        public const int WarnNumber = 13;
        public const string Error = "ERROR";
        public const int ErrorNumber = 17;
    }
}

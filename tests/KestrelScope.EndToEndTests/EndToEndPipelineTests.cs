using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace KestrelScope.EndToEndTests;

public class EndToEndPipelineTests : IAsyncLifetime
{
    private readonly HttpClient _http = new();
    private readonly string _kestrelScopeUrl;
    private readonly string _sampleServiceUrl;
    private Process? _kestrelProcess;
    private Process? _sampleProcess;

    public EndToEndPipelineTests()
    {
        _kestrelScopeUrl = Environment.GetEnvironmentVariable("KESTRELSCOPE_URL") ?? "http://localhost:5000";
        _sampleServiceUrl = Environment.GetEnvironmentVariable("SAMPLE_SERVICE_URL") ?? "http://localhost:8080";
    }

    public async Task InitializeAsync()
    {
        // 1. Auto-start KestrelScope if not already running on localhost
        if (!await IsServiceHealthyAsync(_kestrelScopeUrl))
        {
            if (_kestrelScopeUrl.Contains("localhost") || _kestrelScopeUrl.Contains("127.0.0.1"))
            {
                _kestrelProcess = StartServiceProcess("KestrelScope.csproj", _kestrelScopeUrl);
                await WaitForHealthyAsync(_kestrelScopeUrl, TimeSpan.FromSeconds(15));
            }
        }

        // 2. Auto-start SampleOrderService if not already running on localhost
        if (!await IsServiceHealthyAsync(_sampleServiceUrl))
        {
            if (_sampleServiceUrl.Contains("localhost") || _sampleServiceUrl.Contains("127.0.0.1"))
            {
                var env = new Dictionary<string, string>
                {
                    ["OTEL_EXPORTER_OTLP_ENDPOINT"] = _kestrelScopeUrl,
                    ["OTEL_SERVICE_NAME"] = "order-service"
                };
                _sampleProcess = StartServiceProcess("tests/SampleOrderService/SampleOrderService.csproj", _sampleServiceUrl, env);
                await WaitForHealthyAsync(_sampleServiceUrl, TimeSpan.FromSeconds(15));
            }
        }
    }

    public Task DisposeAsync()
    {
        bool autoKill = Environment.GetEnvironmentVariable("CI") == "true" 
                     || Environment.GetEnvironmentVariable("E2E_AUTO_CLEANUP") == "true";
        if (autoKill)
        {
            try { _sampleProcess?.Kill(true); } catch { }
            try { _kestrelProcess?.Kill(true); } catch { }
        }
        _sampleProcess?.Dispose();
        _kestrelProcess?.Dispose();
        return Task.CompletedTask;
    }

    private async Task<bool> IsServiceHealthyAsync(string baseUrl)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var res = await _http.GetAsync($"{baseUrl.TrimEnd('/')}/health", cts.Token);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task WaitForHealthyAsync(string baseUrl, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await IsServiceHealthyAsync(baseUrl)) return;
            await Task.Delay(250);
        }
        throw new TimeoutException($"Service at {baseUrl} failed to become healthy within {timeout.TotalSeconds}s.");
    }

    private static string GetBuildConfiguration()
    {
        var envConfig = Environment.GetEnvironmentVariable("DOTNET_CONFIGURATION");
        if (!string.IsNullOrWhiteSpace(envConfig))
        {
            return envConfig;
        }

        var pathParts = AppContext.BaseDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (pathParts.Any(p => string.Equals(p, "Release", StringComparison.OrdinalIgnoreCase)))
        {
            return "Release";
        }

        return "Debug";
    }

    private static Process StartServiceProcess(string projectRelPath, string bindUrl, Dictionary<string, string>? extraEnv = null)
    {
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
            throw new DirectoryNotFoundException("Could not locate repository root containing KestrelScope.sln");

        string config = GetBuildConfiguration();
        string fullProjPath = Path.Combine(repoRoot, projectRelPath);
        var psi = new ProcessStartInfo("dotnet", $"run --project \"{fullProjPath}\" -c {config} --no-build")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["ASPNETCORE_URLS"] = bindUrl;
        if (extraEnv != null)
        {
            foreach (var kv in extraEnv) psi.Environment[kv.Key] = kv.Value;
        }

        var proc = Process.Start(psi);
        if (proc == null) throw new InvalidOperationException($"Failed to launch process for {projectRelPath}");
        return proc;
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "KestrelScope.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public async Task Test1_Services_HealthCheck()
    {
        // 1. Verify KestrelScope is healthy
        var kestrelRes = await _http.GetAsync($"{_kestrelScopeUrl}/health");
        Assert.Equal(HttpStatusCode.OK, kestrelRes.StatusCode);
        var kestrelJson = await kestrelRes.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", kestrelJson);

        // 2. Verify SampleOrderService is healthy
        var sampleRes = await _http.GetAsync($"{_sampleServiceUrl}/health");
        Assert.Equal(HttpStatusCode.OK, sampleRes.StatusCode);
        var sampleJson = await sampleRes.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", sampleJson);
    }

    [Fact]
    public async Task Test2_CreateOrder_EmitsDistributedSpansAndMetrics()
    {
        // Step 1: Send real order request to SampleOrderService
        var orderPayload = JsonSerializer.Serialize(new
        {
            customerId = "CUST-9901",
            totalAmount = 149.50m
        });

        var orderRes = await _http.PostAsync(
            $"{_sampleServiceUrl}/api/orders",
            new StringContent(orderPayload, Encoding.UTF8, "application/json")
        );
        Assert.Equal(HttpStatusCode.Created, orderRes.StatusCode);

        // Step 2: Flush telemetry to ensure data is pushed immediately to KestrelScope
        await _http.PostAsync($"{_sampleServiceUrl}/api/telemetry/flush", null);
        await Task.Delay(500); // Allow SQLite batch write to complete

        // Step 3: Assert KestrelScope registered "order-service"
        var servicesRes = await _http.GetAsync($"{_kestrelScopeUrl}/api/metrics/services");
        Assert.Equal(HttpStatusCode.OK, servicesRes.StatusCode);
        var services = await servicesRes.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(services);
        Assert.Contains("order-service", services);

        // Step 4: Assert metrics recorded in KestrelScope
        var seriesRes = await _http.GetAsync($"{_kestrelScopeUrl}/api/metrics/series?service=order-service&metric=http.server.request.duration&minutes=15");
        Assert.Equal(HttpStatusCode.OK, seriesRes.StatusCode);
        using var seriesDoc = await JsonDocument.ParseAsync(await seriesRes.Content.ReadAsStreamAsync());
        var points = seriesDoc.RootElement.EnumerateArray().ToList();
        Assert.NotEmpty(points);
        Assert.True(points.Last().GetProperty("value").GetDouble() > 0);

        // Step 5: Assert traces recorded in KestrelScope
        var tracesRes = await _http.GetAsync($"{_kestrelScopeUrl}/api/traces?service=order-service&limit=20");
        Assert.Equal(HttpStatusCode.OK, tracesRes.StatusCode);
        using var tracesDoc = await JsonDocument.ParseAsync(await tracesRes.Content.ReadAsStreamAsync());
        var spans = tracesDoc.RootElement.EnumerateArray().ToList();
        Assert.NotEmpty(spans);

        // Find the root span for POST /api/orders
        var rootSpan = spans.FirstOrDefault(s => s.GetProperty("spanName").GetString() == "POST /api/orders");
        Assert.True(rootSpan.ValueKind != JsonValueKind.Undefined, "Root span 'POST /api/orders' should exist.");

        string traceId = rootSpan.GetProperty("traceId").GetString()!;
        string rootSpanId = rootSpan.GetProperty("spanId").GetString()!;

        // Step 6: Assert full hierarchical waterfall for this trace
        var detailRes = await _http.GetAsync($"{_kestrelScopeUrl}/api/traces/{traceId}");
        Assert.Equal(HttpStatusCode.OK, detailRes.StatusCode);
        using var detailDoc = await JsonDocument.ParseAsync(await detailRes.Content.ReadAsStreamAsync());
        var detailSpans = detailDoc.RootElement.EnumerateArray().ToList();

        // Must contain all 4 execution spans in the waterfall:
        var spanNames = detailSpans.Select(s => s.GetProperty("spanName").GetString()).ToList();
        Assert.Contains("POST /api/orders", spanNames);
        Assert.Contains("InventoryService.ValidateStock", spanNames);
        Assert.Contains("PaymentGateway.ChargeCard", spanNames);
        Assert.Contains("OrderRepository.SaveOrder", spanNames);

        // Assert parent-child relationship
        var childSpans = detailSpans.Where(s => s.GetProperty("spanName").GetString() != "POST /api/orders").ToList();
        foreach (var child in childSpans)
        {
            Assert.Equal(rootSpanId, child.GetProperty("parentSpanId").GetString());
        }
    }

    [Fact]
    public async Task Test3_FailedOrder_GeneratesErrorSpanAndTriggersAlert()
    {
        // Step 1: Create an alert rule on KestrelScope for high request duration (> 150ms)
        string webhookSink = Environment.GetEnvironmentVariable("INTERNAL_KESTRELSCOPE_URL") 
            ?? "http://127.0.0.1:5000/api/test-webhook";

        var rulePayload = JsonSerializer.Serialize(new
        {
            name = "E2E High Duration Alert",
            metricName = "http.server.request.duration",
            threshold = 150.0,
            windowMinutes = 5,
            webhookUrl = webhookSink,
            isEnabled = 1
        });

        var createRuleRes = await _http.PostAsync(
            $"{_kestrelScopeUrl}/api/alerts",
            new StringContent(rulePayload, Encoding.UTF8, "application/json")
        );
        Assert.Equal(HttpStatusCode.OK, createRuleRes.StatusCode);

        // Step 2: Trigger order failure endpoint (simulates 550ms latency and Error status)
        var failRes = await _http.PostAsync($"{_sampleServiceUrl}/api/orders/fail", null);
        Assert.Equal(HttpStatusCode.InternalServerError, failRes.StatusCode);

        // Step 3: Flush telemetry
        await _http.PostAsync($"{_sampleServiceUrl}/api/telemetry/flush", null);
        await Task.Delay(500);

        // Step 4: Verify Error span exists in KestrelScope
        var tracesRes = await _http.GetAsync($"{_kestrelScopeUrl}/api/traces?service=order-service&limit=10");
        using var tracesDoc = await JsonDocument.ParseAsync(await tracesRes.Content.ReadAsStreamAsync());
        var errorSpan = tracesDoc.RootElement.EnumerateArray()
            .FirstOrDefault(s => s.GetProperty("spanName").GetString() == "POST /api/orders/fail");

        Assert.True(errorSpan.ValueKind != JsonValueKind.Undefined);
        Assert.Equal("Error", errorSpan.GetProperty("statusCode").GetString());

        // Step 5: Trigger alert engine evaluation
        var evalRes = await _http.PostAsync($"{_kestrelScopeUrl}/api/alerts/evaluate-now", null);
        Assert.Equal(HttpStatusCode.OK, evalRes.StatusCode);

        await Task.Delay(500); // Allow async webhook dispatch

        // Step 6: Verify webhook received by checking KestrelScope test webhook sink
        var webhookRes = await _http.GetAsync($"{_kestrelScopeUrl}/api/test-webhook");
        Assert.Equal(HttpStatusCode.OK, webhookRes.StatusCode);
        using var webhookDoc = await JsonDocument.ParseAsync(await webhookRes.Content.ReadAsStreamAsync());
        var webhooks = webhookDoc.RootElement.EnumerateArray().ToList();

        var triggeredAlert = webhooks.FirstOrDefault(w =>
            w.TryGetProperty("alert", out var a) && a.GetString() == "E2E High Duration Alert" &&
            w.TryGetProperty("state", out var s) && s.GetString() == "FIRING"
        );

        Assert.True(triggeredAlert.ValueKind != JsonValueKind.Undefined, "Webhook should have captured FIRING alert payload.");
    }

    [Fact]
    public async Task Test4_Logs_IngestionAndTraceCorrelation()
    {
        // Step 1: Create a normal order to generate INFO logs
        var orderPayload = JsonSerializer.Serialize(new
        {
            customerId = "CUST-LOGS-77",
            totalAmount = 250.00m
        });

        var orderRes = await _http.PostAsync(
            $"{_sampleServiceUrl}/api/orders",
            new StringContent(orderPayload, Encoding.UTF8, "application/json")
        );
        Assert.Equal(HttpStatusCode.Created, orderRes.StatusCode);

        // Step 2: Trigger order failure to generate WARN and ERROR logs
        var failRes = await _http.PostAsync($"{_sampleServiceUrl}/api/orders/fail", null);
        Assert.Equal(HttpStatusCode.InternalServerError, failRes.StatusCode);

        // Step 3: Flush telemetry
        await _http.PostAsync($"{_sampleServiceUrl}/api/telemetry/flush", null);
        await Task.Delay(500);

        // Step 4: Query logs for order-service from KestrelScope
        var logsRes = await _http.GetAsync($"{_kestrelScopeUrl}/api/logs?service=order-service&limit=50");
        Assert.Equal(HttpStatusCode.OK, logsRes.StatusCode);
        using var logsDoc = await JsonDocument.ParseAsync(await logsRes.Content.ReadAsStreamAsync());
        var logs = logsDoc.RootElement.EnumerateArray().ToList();
        Assert.NotEmpty(logs);

        // Step 5: Assert existence of INFO, WARN, and ERROR logs
        var severities = logs.Select(l => l.GetProperty("severityText").GetString()).ToList();
        Assert.Contains("INFO", severities);
        Assert.Contains("WARN", severities);
        Assert.Contains("ERROR", severities);

        // Step 6: Assert text search query filtering
        var searchRes = await _http.GetAsync($"{_kestrelScopeUrl}/api/logs?service=order-service&query=declined");
        Assert.Equal(HttpStatusCode.OK, searchRes.StatusCode);
        using var searchDoc = await JsonDocument.ParseAsync(await searchRes.Content.ReadAsStreamAsync());
        var searchResults = searchDoc.RootElement.EnumerateArray().ToList();
        Assert.NotEmpty(searchResults);
        Assert.Contains("Payment declined", searchResults.First().GetProperty("body").GetString());

        // Step 7: Assert bidirectional trace correlation
        var errorLog = logs.FirstOrDefault(l => l.GetProperty("severityText").GetString() == "ERROR");
        Assert.True(errorLog.ValueKind != JsonValueKind.Undefined);
        string? traceId = errorLog.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(traceId));

        var traceLogsRes = await _http.GetAsync($"{_kestrelScopeUrl}/api/logs?traceId={traceId}");
        Assert.Equal(HttpStatusCode.OK, traceLogsRes.StatusCode);
        using var traceLogsDoc = await JsonDocument.ParseAsync(await traceLogsRes.Content.ReadAsStreamAsync());
        var correlatedLogs = traceLogsDoc.RootElement.EnumerateArray().ToList();
        Assert.NotEmpty(correlatedLogs);
        Assert.All(correlatedLogs, l => Assert.Equal(traceId, l.GetProperty("traceId").GetString()));

        // Also verify the trace itself exists in the Traces table
        var traceDetailRes = await _http.GetAsync($"{_kestrelScopeUrl}/api/traces/{traceId}");
        Assert.Equal(HttpStatusCode.OK, traceDetailRes.StatusCode);
        using var traceDetailDoc = await JsonDocument.ParseAsync(await traceDetailRes.Content.ReadAsStreamAsync());
        var traceSpans = traceDetailDoc.RootElement.EnumerateArray().ToList();
        Assert.NotEmpty(traceSpans);
    }
}

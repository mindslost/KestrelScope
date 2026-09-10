using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KestrelScope.Tools;

public class SyntheticTelemetryGenerator
{
    public static async Task RunAsync(string baseUrl = "http://localhost:5000", int count = 10, CancellationToken ct = default)
    {
        using var client = new HttpClient();
        var rand = new Random();
        string metricsEndpoint = $"{baseUrl.TrimEnd('/')}/v1/metrics";
        string tracesEndpoint = $"{baseUrl.TrimEnd('/')}/v1/traces";

        Console.WriteLine($"Starting Synthetic Telemetry Generator -> {baseUrl}");

        for (int i = 0; i < count && !ct.IsCancellationRequested; i++)
        {
            double latency = 100 + rand.NextDouble() * 400;
            long nowNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000;

            // 1. Emit Metric Payload
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
                                new { key = "service.name", value = new { stringValue = "order-processor-service" } }
                            }
                        },
                        scopeMetrics = new[]
                        {
                            new
                            {
                                metrics = new object[]
                                {
                                    new
                                    {
                                        name = "http.server.duration",
                                        sum = new
                                        {
                                            dataPoints = new[]
                                            {
                                                new { asDouble = latency, timeUnixNano = nowNano.ToString() }
                                            }
                                        }
                                    },
                                    new
                                    {
                                        name = "process.memory.usage",
                                        gauge = new
                                        {
                                            dataPoints = new[]
                                            {
                                                new { asDouble = 256.0 + rand.NextDouble() * 64, timeUnixNano = nowNano.ToString() }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var metricJson = JsonSerializer.Serialize(metricPayload);
            var metricResponse = await client.PostAsync(
                metricsEndpoint, 
                new StringContent(metricJson, Encoding.UTF8, "application/json"),
                ct
            );

            // 2. Emit Trace Payload
            string traceId = Guid.NewGuid().ToString("N");
            string rootSpanId = Guid.NewGuid().ToString("N")[..16];
            string childSpanId = Guid.NewGuid().ToString("N")[..16];
            long endNano = nowNano + (long)(latency * 1_000_000);

            var tracePayload = new
            {
                resourceSpans = new[]
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
                        scopeSpans = new[]
                        {
                            new
                            {
                                spans = new object[]
                                {
                                    new
                                    {
                                        traceId,
                                        spanId = rootSpanId,
                                        parentSpanId = (string?)null,
                                        name = "POST /orders",
                                        startTimeUnixNano = nowNano.ToString(),
                                        endTimeUnixNano = endNano.ToString(),
                                        status = new { code = 1 }
                                    },
                                    new
                                    {
                                        traceId,
                                        spanId = childSpanId,
                                        parentSpanId = (string?)rootSpanId,
                                        name = "SELECT * FROM orders",
                                        startTimeUnixNano = (nowNano + 10_000_000).ToString(),
                                        endTimeUnixNano = (nowNano + 50_000_000).ToString(),
                                        status = new { code = 1 }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var traceJson = JsonSerializer.Serialize(tracePayload);
            var traceResponse = await client.PostAsync(
                tracesEndpoint,
                new StringContent(traceJson, Encoding.UTF8, "application/json"),
                ct
            );

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sample {i + 1}/{count}: Metric={metricResponse.StatusCode} ({latency:F2}ms), Trace={traceResponse.StatusCode}");
            await Task.Delay(500, ct);
        }
    }
}


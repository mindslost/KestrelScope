using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KestrelScope.Constants;
using KestrelScope.Models;

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
            long nowNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * TelemetryConstants.Conversion.NanoPerMilli;

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
                                new { key = TelemetryConstants.OtlpAttributes.ServiceName, value = new { stringValue = TelemetryConstants.ServiceNames.OrderProcessorService } }
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
                                        name = TelemetryConstants.MetricNames.HttpServerRequestDuration,
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
                                        name = TelemetryConstants.MetricNames.ProcessMemoryUsage,
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
            long endNano = nowNano + (long)(latency * TelemetryConstants.Conversion.NanoPerMilli);

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
                                new { key = TelemetryConstants.OtlpAttributes.ServiceName, value = new { stringValue = TelemetryConstants.ServiceNames.OrderProcessorService } }
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
                                        name = TelemetryConstants.SpanNames.CreateOrder,
                                        startTimeUnixNano = nowNano.ToString(),
                                        endTimeUnixNano = endNano.ToString(),
                                        status = new { code = (int)SpanStatusCode.Ok }
                                    },
                                    new
                                    {
                                        traceId,
                                        spanId = childSpanId,
                                        parentSpanId = (string?)rootSpanId,
                                        name = "SELECT * FROM orders",
                                        startTimeUnixNano = (nowNano + 10 * TelemetryConstants.Conversion.NanoPerMilli).ToString(),
                                        endTimeUnixNano = (nowNano + 50 * TelemetryConstants.Conversion.NanoPerMilli).ToString(),
                                        status = new { code = (int)SpanStatusCode.Ok }
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


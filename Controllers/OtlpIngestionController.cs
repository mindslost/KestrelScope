using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KestrelScope.Controllers;

[ApiController]
[Route("v1")]
public class OtlpIngestionController : ControllerBase
{
    private readonly string _dbConn;
    private readonly ILogger<OtlpIngestionController> _logger;

    public OtlpIngestionController(IConfiguration config, ILogger<OtlpIngestionController> logger)
    {
        _dbConn = config.GetConnectionString("DefaultConnection") ?? "Data Source=observability.db;";
        _logger = logger;
    }

    [HttpPost("metrics")]
    public async Task<IActionResult> IngestMetrics([FromBody] JsonElement payload)
    {
        if (!payload.TryGetProperty("resourceMetrics", out var resourceMetrics) || 
            resourceMetrics.ValueKind != JsonValueKind.Array)
        {
            return BadRequest(new { error = "Invalid OTLP payload: missing or invalid resourceMetrics" });
        }

        int sampleCount = 0;

        using var connection = new SqliteConnection(_dbConn);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO MetricSamples (ServiceName, MetricName, Value, Timestamp) 
            VALUES (@service, @metric, @val, @time);";

        var paramService = cmd.Parameters.Add("@service", SqliteType.Text);
        var paramMetric = cmd.Parameters.Add("@metric", SqliteType.Text);
        var paramVal = cmd.Parameters.Add("@val", SqliteType.Real);
        var paramTime = cmd.Parameters.Add("@time", SqliteType.Text);

        foreach (var rm in resourceMetrics.EnumerateArray())
        {
            string serviceName = ExtractServiceName(rm);

            if (!rm.TryGetProperty("scopeMetrics", out var scopeMetrics) || scopeMetrics.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var sm in scopeMetrics.EnumerateArray())
            {
                if (!sm.TryGetProperty("metrics", out var metrics) || metrics.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var metric in metrics.EnumerateArray())
                {
                    if (!metric.TryGetProperty("name", out var nameProp))
                        continue;

                    string metricName = nameProp.GetString() ?? "unnamed_metric";

                    // Handle Sum metrics
                    if (metric.TryGetProperty("sum", out var sum) &&
                        sum.TryGetProperty("dataPoints", out var sumDataPoints) &&
                        sumDataPoints.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var dp in sumDataPoints.EnumerateArray())
                        {
                            if (TryExtractSample(dp, out double val, out DateTime timestamp))
                            {
                                paramService.Value = serviceName;
                                paramMetric.Value = metricName;
                                paramVal.Value = val;
                                paramTime.Value = timestamp.ToString("o");
                                await cmd.ExecuteNonQueryAsync();
                                sampleCount++;
                            }
                        }
                    }

                    // Handle Gauge metrics
                    if (metric.TryGetProperty("gauge", out var gauge) &&
                        gauge.TryGetProperty("dataPoints", out var gaugeDataPoints) &&
                        gaugeDataPoints.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var dp in gaugeDataPoints.EnumerateArray())
                        {
                            if (TryExtractSample(dp, out double val, out DateTime timestamp))
                            {
                                paramService.Value = serviceName;
                                paramMetric.Value = metricName;
                                paramVal.Value = val;
                                paramTime.Value = timestamp.ToString("o");
                                await cmd.ExecuteNonQueryAsync();
                                sampleCount++;
                            }
                        }
                    }
                }
            }
        }

        await transaction.CommitAsync();
        _logger.LogInformation("Ingested {Count} metric samples.", sampleCount);
        return Ok(new { status = "success", ingested = sampleCount });
    }

    [HttpPost("traces")]
    public async Task<IActionResult> IngestTraces([FromBody] JsonElement payload)
    {
        if (!payload.TryGetProperty("resourceSpans", out var resourceSpans) ||
            resourceSpans.ValueKind != JsonValueKind.Array)
        {
            return BadRequest(new { error = "Invalid OTLP payload: missing or invalid resourceSpans" });
        }

        int spanCount = 0;

        using var connection = new SqliteConnection(_dbConn);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT OR REPLACE INTO Traces (TraceId, SpanId, ParentSpanId, ServiceName, SpanName, DurationMs, StatusCode, Timestamp)
            VALUES (@traceId, @spanId, @parentSpanId, @service, @name, @duration, @status, @time);";

        var pTraceId = cmd.Parameters.Add("@traceId", SqliteType.Text);
        var pSpanId = cmd.Parameters.Add("@spanId", SqliteType.Text);
        var pParentSpanId = cmd.Parameters.Add("@parentSpanId", SqliteType.Text);
        var pService = cmd.Parameters.Add("@service", SqliteType.Text);
        var pName = cmd.Parameters.Add("@name", SqliteType.Text);
        var pDuration = cmd.Parameters.Add("@duration", SqliteType.Real);
        var pStatus = cmd.Parameters.Add("@status", SqliteType.Text);
        var pTime = cmd.Parameters.Add("@time", SqliteType.Text);

        foreach (var rs in resourceSpans.EnumerateArray())
        {
            string serviceName = ExtractServiceName(rs);

            if (!rs.TryGetProperty("scopeSpans", out var scopeSpans) || scopeSpans.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var ss in scopeSpans.EnumerateArray())
            {
                if (!ss.TryGetProperty("spans", out var spans) || spans.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var span in spans.EnumerateArray())
                {
                    string traceId = span.TryGetProperty("traceId", out var tId) ? tId.GetString() ?? "" : "";
                    string spanId = span.TryGetProperty("spanId", out var sId) ? sId.GetString() ?? "" : "";
                    string? parentSpanId = span.TryGetProperty("parentSpanId", out var pId) ? pId.GetString() : null;
                    string spanName = span.TryGetProperty("name", out var n) ? n.GetString() ?? "unnamed_span" : "unnamed_span";

                    if (string.IsNullOrWhiteSpace(traceId) || string.IsNullOrWhiteSpace(spanId))
                        continue;

                    ulong startNano = ParseNano(span, "startTimeUnixNano");
                    ulong endNano = ParseNano(span, "endTimeUnixNano");

                    double durationMs = (endNano > startNano && startNano > 0)
                        ? (endNano - startNano) / 1_000_000.0
                        : 0.0;

                    DateTime timestamp = startNano > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds((long)(startNano / 1_000_000)).UtcDateTime
                        : DateTime.UtcNow;

                    string statusCode = ExtractStatusCode(span);

                    pTraceId.Value = traceId;
                    pSpanId.Value = spanId;
                    pParentSpanId.Value = string.IsNullOrEmpty(parentSpanId) ? DBNull.Value : parentSpanId;
                    pService.Value = serviceName;
                    pName.Value = spanName;
                    pDuration.Value = durationMs;
                    pStatus.Value = statusCode;
                    pTime.Value = timestamp.ToString("o");

                    await cmd.ExecuteNonQueryAsync();
                    spanCount++;
                }
            }
        }

        await transaction.CommitAsync();
        _logger.LogInformation("Ingested {Count} trace spans.", spanCount);
        return Ok(new { status = "success", ingested = spanCount });
    }

    [HttpPost("logs")]
    public async Task<IActionResult> IngestLogs([FromBody] JsonElement payload)
    {
        if (!payload.TryGetProperty("resourceLogs", out var resourceLogs) || 
            resourceLogs.ValueKind != JsonValueKind.Array)
        {
            return BadRequest(new { error = "Invalid OTLP payload: missing or invalid resourceLogs" });
        }

        int logCount = 0;

        using var connection = new SqliteConnection(_dbConn);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO Logs (Timestamp, TraceId, SpanId, ServiceName, SeverityText, SeverityNumber, Body, AttributesJson)
            VALUES (@time, @traceId, @spanId, @service, @sevText, @sevNum, @body, @attrs);";

        var pTime = cmd.Parameters.Add("@time", SqliteType.Text);
        var pTraceId = cmd.Parameters.Add("@traceId", SqliteType.Text);
        var pSpanId = cmd.Parameters.Add("@spanId", SqliteType.Text);
        var pService = cmd.Parameters.Add("@service", SqliteType.Text);
        var pSevText = cmd.Parameters.Add("@sevText", SqliteType.Text);
        var pSevNum = cmd.Parameters.Add("@sevNum", SqliteType.Integer);
        var pBody = cmd.Parameters.Add("@body", SqliteType.Text);
        var pAttrs = cmd.Parameters.Add("@attrs", SqliteType.Text);

        foreach (var rl in resourceLogs.EnumerateArray())
        {
            string serviceName = ExtractServiceName(rl);

            if (!rl.TryGetProperty("scopeLogs", out var scopeLogs) || scopeLogs.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var sl in scopeLogs.EnumerateArray())
            {
                if (!sl.TryGetProperty("logRecords", out var logRecords) || logRecords.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var log in logRecords.EnumerateArray())
                {
                    ulong timeNano = ParseNano(log, "timeUnixNano");
                    if (timeNano == 0) timeNano = ParseNano(log, "observedTimeUnixNano");
                    DateTime timestamp = timeNano > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds((long)(timeNano / 1_000_000)).UtcDateTime
                        : DateTime.UtcNow;

                    string? traceId = log.TryGetProperty("traceId", out var tId) ? tId.GetString() : null;
                    string? spanId = log.TryGetProperty("spanId", out var sId) ? sId.GetString() : null;

                    string sevText = log.TryGetProperty("severityText", out var st) ? (st.GetString() ?? "INFO") : "INFO";
                    int sevNum = log.TryGetProperty("severityNumber", out var sn) && sn.TryGetInt32(out int sVal) ? sVal : 9;

                    string body = "";
                    if (log.TryGetProperty("body", out var bProp))
                    {
                        if (bProp.ValueKind == JsonValueKind.String)
                        {
                            body = bProp.GetString() ?? "";
                        }
                        else if (bProp.ValueKind == JsonValueKind.Object && bProp.TryGetProperty("stringValue", out var strVal))
                        {
                            body = strVal.GetString() ?? "";
                        }
                        else
                        {
                            body = bProp.GetRawText();
                        }
                    }

                    string attrsJson = "{}";
                    if (log.TryGetProperty("attributes", out var attrs))
                    {
                        attrsJson = attrs.GetRawText();
                    }

                    pTime.Value = timestamp.ToString("o");
                    pTraceId.Value = string.IsNullOrEmpty(traceId) ? DBNull.Value : traceId;
                    pSpanId.Value = string.IsNullOrEmpty(spanId) ? DBNull.Value : spanId;
                    pService.Value = serviceName;
                    pSevText.Value = sevText.ToUpperInvariant();
                    pSevNum.Value = sevNum;
                    pBody.Value = body;
                    pAttrs.Value = attrsJson;

                    await cmd.ExecuteNonQueryAsync();
                    logCount++;
                }
            }
        }

        await transaction.CommitAsync();
        _logger.LogInformation("Ingested {Count} log records.", logCount);
        return Ok(new { status = "success", ingested = logCount });
    }

    private static string ExtractServiceName(JsonElement root)
    {
        if (root.TryGetProperty("resource", out var res) &&
            res.TryGetProperty("attributes", out var attrs) &&
            attrs.ValueKind == JsonValueKind.Array)
        {
            foreach (var attr in attrs.EnumerateArray())
            {
                if (attr.TryGetProperty("key", out var key) && key.GetString() == "service.name" &&
                    attr.TryGetProperty("value", out var val))
                {
                    if (val.TryGetProperty("stringValue", out var str))
                        return str.GetString() ?? "unknown-service";
                }
            }
        }
        return "unknown-service";
    }

    private static bool TryExtractSample(JsonElement dp, out double val, out DateTime timestamp)
    {
        val = 0.0;
        timestamp = DateTime.UtcNow;

        if (dp.TryGetProperty("asDouble", out var d))
        {
            val = d.GetDouble();
        }
        else if (dp.TryGetProperty("asInt", out var i))
        {
            if (i.ValueKind == JsonValueKind.Number)
                val = i.GetInt64();
            else if (i.ValueKind == JsonValueKind.String && long.TryParse(i.GetString(), out var lVal))
                val = lVal;
            else
                return false;
        }
        else
        {
            return false;
        }

        ulong nano = ParseNano(dp, "timeUnixNano");
        if (nano > 0)
        {
            timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)(nano / 1_000_000)).UtcDateTime;
        }

        return true;
    }

    private static ulong ParseNano(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetUInt64(out var n))
                return n;
            if (prop.ValueKind == JsonValueKind.String && ulong.TryParse(prop.GetString(), out var sNano))
                return sNano;
        }
        return 0;
    }

    private static string ExtractStatusCode(JsonElement span)
    {
        if (span.TryGetProperty("status", out var status))
        {
            if (status.TryGetProperty("code", out var code))
            {
                if (code.ValueKind == JsonValueKind.Number)
                {
                    return code.GetInt32() switch
                    {
                        1 => "Ok",
                        2 => "Error",
                        _ => "Unset"
                    };
                }
                if (code.ValueKind == JsonValueKind.String)
                {
                    string str = code.GetString() ?? "";
                    if (str.Contains("ERROR", StringComparison.OrdinalIgnoreCase)) return "Error";
                    if (str.Contains("OK", StringComparison.OrdinalIgnoreCase)) return "Ok";
                    return str;
                }
            }
        }
        return "Unset";
    }
}


using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using KestrelScope.Constants;
using KestrelScope.Models;

namespace KestrelScope.Controllers;

[ApiController]
[Route("v1")]
public class OtlpIngestionController : ControllerBase
{
    private readonly string _dbConn;
    private readonly ILogger<OtlpIngestionController> _logger;

    public OtlpIngestionController(IConfiguration config, ILogger<OtlpIngestionController> logger)
    {
        _dbConn = config.GetConnectionString("DefaultConnection") ?? AppConstants.Database.DefaultConnectionString;
        _logger = logger;
    }

    [HttpPost("metrics")]
    public async Task<IActionResult> IngestMetrics([FromBody] JsonElement payload)
    {
        if (!payload.TryGetProperty(TelemetryConstants.OtlpFields.ResourceMetrics, out var resourceMetrics) || 
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

            if (!rm.TryGetProperty(TelemetryConstants.OtlpFields.ScopeMetrics, out var scopeMetrics) || scopeMetrics.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var sm in scopeMetrics.EnumerateArray())
            {
                if (!sm.TryGetProperty(TelemetryConstants.OtlpFields.Metrics, out var metrics) || metrics.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var metric in metrics.EnumerateArray())
                {
                    if (!metric.TryGetProperty(TelemetryConstants.OtlpFields.Name, out var nameProp))
                        continue;

                    string metricName = nameProp.GetString() ?? TelemetryConstants.SpanNames.UnnamedMetric;

                    foreach (var dp in GetDataPoints(metric))
                    {
                        if (!TryExtractSample(dp, out double val, out DateTime timestamp))
                            continue;

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

        await transaction.CommitAsync();
        _logger.LogInformation("Ingested {Count} metric samples.", sampleCount);
        return Ok(new { status = "success", ingested = sampleCount });
    }

    [HttpPost("traces")]
    public async Task<IActionResult> IngestTraces([FromBody] JsonElement payload)
    {
        if (!payload.TryGetProperty(TelemetryConstants.OtlpFields.ResourceSpans, out var resourceSpans) ||
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

            if (!rs.TryGetProperty(TelemetryConstants.OtlpFields.ScopeSpans, out var scopeSpans) || scopeSpans.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var ss in scopeSpans.EnumerateArray())
            {
                if (!ss.TryGetProperty(TelemetryConstants.OtlpFields.Spans, out var spans) || spans.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var span in spans.EnumerateArray())
                {
                    string traceId = span.TryGetProperty(TelemetryConstants.OtlpFields.TraceId, out var tId) ? tId.GetString() ?? "" : "";
                    string spanId = span.TryGetProperty(TelemetryConstants.OtlpFields.SpanId, out var sId) ? sId.GetString() ?? "" : "";
                    string? parentSpanId = span.TryGetProperty(TelemetryConstants.OtlpFields.ParentSpanId, out var pId) ? pId.GetString() : null;
                    string spanName = span.TryGetProperty(TelemetryConstants.OtlpFields.Name, out var n) ? n.GetString() ?? TelemetryConstants.SpanNames.UnnamedSpan : TelemetryConstants.SpanNames.UnnamedSpan;

                    if (string.IsNullOrWhiteSpace(traceId) || string.IsNullOrWhiteSpace(spanId))
                        continue;

                    ulong startNano = ParseNano(span, TelemetryConstants.OtlpFields.StartTimeUnixNano);
                    ulong endNano = ParseNano(span, TelemetryConstants.OtlpFields.EndTimeUnixNano);

                    double durationMs = (endNano > startNano && startNano > 0)
                        ? (endNano - startNano) / TelemetryConstants.Conversion.NanoToMilli
                        : 0.0;

                    DateTime timestamp = startNano > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds((long)(startNano / TelemetryConstants.Conversion.NanoPerMilli)).UtcDateTime
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
        if (!payload.TryGetProperty(TelemetryConstants.OtlpFields.ResourceLogs, out var resourceLogs) || 
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

            if (!rl.TryGetProperty(TelemetryConstants.OtlpFields.ScopeLogs, out var scopeLogs) || scopeLogs.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var sl in scopeLogs.EnumerateArray())
            {
                if (!sl.TryGetProperty(TelemetryConstants.OtlpFields.LogRecords, out var logRecords) || logRecords.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var log in logRecords.EnumerateArray())
                {
                    ulong timeNano = ParseNano(log, TelemetryConstants.OtlpFields.TimeUnixNano);
                    if (timeNano == 0) timeNano = ParseNano(log, TelemetryConstants.OtlpFields.ObservedTimeUnixNano);
                    DateTime timestamp = timeNano > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds((long)(timeNano / TelemetryConstants.Conversion.NanoPerMilli)).UtcDateTime
                        : DateTime.UtcNow;

                    string? traceId = log.TryGetProperty(TelemetryConstants.OtlpFields.TraceId, out var tId) ? tId.GetString() : null;
                    string? spanId = log.TryGetProperty(TelemetryConstants.OtlpFields.SpanId, out var sId) ? sId.GetString() : null;

                    string sevText = log.TryGetProperty(TelemetryConstants.OtlpFields.SeverityText, out var st) 
                        ? (st.GetString() ?? TelemetryConstants.SeverityText.Info) 
                        : TelemetryConstants.SeverityText.Info;
                    int sevNum = log.TryGetProperty(TelemetryConstants.OtlpFields.SeverityNumber, out var sn) && sn.TryGetInt32(out int sVal) 
                        ? sVal 
                        : (int)OtelSeverity.Info;

                    string body = ExtractLogBody(log);
                    string attrsJson = ExtractLogAttributes(log);

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

    private static IEnumerable<JsonElement> GetDataPoints(JsonElement metric)
    {
        if (metric.TryGetProperty(TelemetryConstants.OtlpFields.Sum, out var sum) &&
            sum.TryGetProperty(TelemetryConstants.OtlpFields.DataPoints, out var sumDataPoints) &&
            sumDataPoints.ValueKind == JsonValueKind.Array)
        {
            foreach (var dp in sumDataPoints.EnumerateArray())
                yield return dp;
        }

        if (metric.TryGetProperty(TelemetryConstants.OtlpFields.Gauge, out var gauge) &&
            gauge.TryGetProperty(TelemetryConstants.OtlpFields.DataPoints, out var gaugeDataPoints) &&
            gaugeDataPoints.ValueKind == JsonValueKind.Array)
        {
            foreach (var dp in gaugeDataPoints.EnumerateArray())
                yield return dp;
        }
    }

    private static string ExtractLogBody(JsonElement log)
    {
        if (!log.TryGetProperty(TelemetryConstants.OtlpFields.Body, out var bProp))
            return string.Empty;

        return bProp.ValueKind switch
        {
            JsonValueKind.String => bProp.GetString() ?? string.Empty,
            JsonValueKind.Object when bProp.TryGetProperty(TelemetryConstants.OtlpFields.StringValue, out var strVal) => strVal.GetString() ?? string.Empty,
            _ => bProp.GetRawText()
        };
    }

    private static string ExtractLogAttributes(JsonElement log)
    {
        return log.TryGetProperty(TelemetryConstants.OtlpFields.Attributes, out var attrs)
            ? attrs.GetRawText()
            : "{}";
    }

    private static string ExtractServiceName(JsonElement root)
    {
        if (!root.TryGetProperty(TelemetryConstants.OtlpFields.Resource, out var res) ||
            !res.TryGetProperty(TelemetryConstants.OtlpFields.Attributes, out var attrs) ||
            attrs.ValueKind != JsonValueKind.Array)
        {
            return TelemetryConstants.ServiceNames.UnknownService;
        }

        foreach (var attr in attrs.EnumerateArray())
        {
            if (attr.TryGetProperty(TelemetryConstants.OtlpFields.Key, out var key) && 
                key.GetString() == TelemetryConstants.OtlpAttributes.ServiceName &&
                attr.TryGetProperty(TelemetryConstants.OtlpFields.Value, out var val) &&
                val.TryGetProperty(TelemetryConstants.OtlpFields.StringValue, out var str))
            {
                return str.GetString() ?? TelemetryConstants.ServiceNames.UnknownService;
            }
        }

        return TelemetryConstants.ServiceNames.UnknownService;
    }

    private static bool TryExtractSample(JsonElement dp, out double val, out DateTime timestamp)
    {
        val = 0.0;
        timestamp = DateTime.UtcNow;

        if (dp.TryGetProperty(TelemetryConstants.OtlpFields.AsDouble, out var d))
        {
            val = d.GetDouble();
        }
        else if (dp.TryGetProperty(TelemetryConstants.OtlpFields.AsInt, out var i))
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

        ulong nano = ParseNano(dp, TelemetryConstants.OtlpFields.TimeUnixNano);
        if (nano > 0)
        {
            timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)(nano / TelemetryConstants.Conversion.NanoPerMilli)).UtcDateTime;
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
        if (!span.TryGetProperty(TelemetryConstants.OtlpFields.Status, out var status) ||
            !status.TryGetProperty(TelemetryConstants.OtlpFields.Code, out var code))
        {
            return TelemetryConstants.SpanStatusNames.Unset;
        }

        if (code.ValueKind == JsonValueKind.Number)
        {
            return code.GetInt32() switch
            {
                (int)SpanStatusCode.Ok => TelemetryConstants.SpanStatusNames.Ok,
                (int)SpanStatusCode.Error => TelemetryConstants.SpanStatusNames.Error,
                _ => TelemetryConstants.SpanStatusNames.Unset
            };
        }

        if (code.ValueKind == JsonValueKind.String)
        {
            string str = code.GetString() ?? string.Empty;
            if (str.Contains(TelemetryConstants.SpanStatusNames.Error, StringComparison.OrdinalIgnoreCase)) 
                return TelemetryConstants.SpanStatusNames.Error;
            if (str.Contains(TelemetryConstants.SpanStatusNames.Ok, StringComparison.OrdinalIgnoreCase)) 
                return TelemetryConstants.SpanStatusNames.Ok;
            return str;
        }

        return TelemetryConstants.SpanStatusNames.Unset;
    }
}

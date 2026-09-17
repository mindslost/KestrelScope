using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KestrelScope.Constants;
using KestrelScope.Models;

namespace KestrelScope.Controllers;

[ApiController]
[Authorize]
[Route("api/topology")]
public class TopologyController : ControllerBase
{
    private readonly string _dbConn;

    public TopologyController(IConfiguration config)
    {
        _dbConn = config.GetConnectionString("DefaultConnection") ?? AppConstants.Database.DefaultConnectionString;
    }

    [HttpGet("flow-map")]
    public async Task<IActionResult> GetFlowMap(
        [FromQuery] int minutes = AppConstants.QueryDefaults.DefaultMetricsRecentMinutes,
        [FromQuery] string application = "ECommerce")
    {
        if (minutes <= 0) minutes = AppConstants.QueryDefaults.DefaultMetricsRecentMinutes;
        if (minutes > TelemetryConstants.SeedDefaults.MaxHistoryMinutes) minutes = TelemetryConstants.SeedDefaults.MaxHistoryMinutes;

        var windowStart = DateTime.UtcNow.AddMinutes(-minutes).ToString("o");

        using var conn = new SqliteConnection(_dbConn);
        await conn.OpenAsync();

        // 1. Query aggregate trace telemetry per service within window
        var serviceAggregates = (await conn.QueryAsync<(string ServiceName, int TotalCalls, double AvgDuration, int ErrorCalls)>(
            @"SELECT ServiceName, 
                     COUNT(*) AS TotalCalls, 
                     AVG(DurationMs) AS AvgDuration,
                     SUM(CASE WHEN StatusCode = @ErrorStatus THEN 1 ELSE 0 END) AS ErrorCalls
              FROM Traces
              WHERE Timestamp >= @windowStart
              GROUP BY ServiceName;",
            new { windowStart, ErrorStatus = TelemetryConstants.SpanStatusNames.Error }
        )).ToDictionary(x => x.ServiceName, x => x);

        // 2. Query edge calls from parent-child span links within window
        var edgeAggregates = (await conn.QueryAsync<(string Source, string Target, int TotalCalls, double AvgDuration, int ErrorCalls)>(
            @"SELECT p.ServiceName AS Source, 
                     c.ServiceName AS Target,
                     COUNT(*) AS TotalCalls,
                     AVG(c.DurationMs) AS AvgDuration,
                     SUM(CASE WHEN c.StatusCode = @ErrorStatus THEN 1 ELSE 0 END) AS ErrorCalls
              FROM Traces c
              INNER JOIN Traces p ON c.TraceId = p.TraceId AND c.ParentSpanId = p.SpanId
              WHERE c.Timestamp >= @windowStart AND p.ServiceName != c.ServiceName
              GROUP BY p.ServiceName, c.ServiceName;",
            new { windowStart, ErrorStatus = TelemetryConstants.SpanStatusNames.Error }
        )).ToDictionary(x => $"{x.Source}->{x.Target}", x => x);

        // 3. Define Baseline Multi-Service Nodes matching AppDynamics Reference
        var baseNodes = new List<TopologyNodeDto>
        {
            new() { Id = TelemetryConstants.ServiceNames.WebTierServices, Label = TelemetryConstants.ServiceNames.WebTierServices, Type = TelemetryConstants.TopologyConstants.TypeService, NodeCount = 1, TechBadge = TelemetryConstants.TopologyConstants.TechJava, X = 160, Y = 220, CallsPerMin = 14, AvgLatencyMs = 95, ErrorRatePercent = 0.0 },
            new() { Id = TelemetryConstants.ServiceNames.ECommerceServices, Label = TelemetryConstants.ServiceNames.ECommerceServices, Type = TelemetryConstants.TopologyConstants.TypeService, NodeCount = 3, TechBadge = TelemetryConstants.TopologyConstants.TechJava, X = 430, Y = 220, CallsPerMin = 620, AvgLatencyMs = 42, ErrorRatePercent = 0.2 },
            new() { Id = TelemetryConstants.ServiceNames.InventoryServices, Label = TelemetryConstants.ServiceNames.InventoryServices, Type = TelemetryConstants.TopologyConstants.TypeService, NodeCount = 1, TechBadge = TelemetryConstants.TopologyConstants.TechJava, X = 740, Y = 130, CallsPerMin = 435, AvgLatencyMs = 18, ErrorRatePercent = 0.0 },
            new() { Id = TelemetryConstants.ServiceNames.AddressServices, Label = TelemetryConstants.ServiceNames.AddressServices, Type = TelemetryConstants.TopologyConstants.TypeService, NodeCount = 1, TechBadge = TelemetryConstants.TopologyConstants.TechJava, X = 740, Y = 320, CallsPerMin = 192, AvgLatencyMs = 28, ErrorRatePercent = 0.0 },
            new() { Id = TelemetryConstants.ServiceNames.OrderProcessingServices, Label = TelemetryConstants.ServiceNames.OrderProcessingServices, Type = TelemetryConstants.TopologyConstants.TypeService, NodeCount = 1, TechBadge = TelemetryConstants.TopologyConstants.TechJava, X = 430, Y = 460, CallsPerMin = 23, AvgLatencyMs = 85, ErrorRatePercent = 0.0 },
            new() { Id = TelemetryConstants.ServiceNames.CustomerSurveyServices, Label = TelemetryConstants.ServiceNames.CustomerSurveyServices, Type = TelemetryConstants.TopologyConstants.TypeService, NodeCount = 1, TechBadge = TelemetryConstants.TopologyConstants.TechJava, X = 860, Y = 530, CallsPerMin = 6, AvgLatencyMs = 120, ErrorRatePercent = 0.0 },
            
            // Databases
            new() { Id = TelemetryConstants.BackendNames.InventoryMySql, Label = TelemetryConstants.BackendNames.InventoryMySql, Type = TelemetryConstants.TopologyConstants.TypeDatabase, NodeCount = 1, TechBadge = TelemetryConstants.TopologyConstants.TechMySql, X = 980, Y = 80, CallsPerMin = 435, AvgLatencyMs = 4, ErrorRatePercent = 0.0 },
            new() { Id = TelemetryConstants.BackendNames.OracleDbProduction, Label = TelemetryConstants.BackendNames.OracleDbProduction, Type = TelemetryConstants.TopologyConstants.TypeDatabase, NodeCount = 1, TechBadge = TelemetryConstants.TopologyConstants.TechOracle, X = 980, Y = 210, CallsPerMin = 107, AvgLatencyMs = 8, ErrorRatePercent = 0.0 },
            new() { Id = TelemetryConstants.BackendNames.MongoDbTest, Label = TelemetryConstants.BackendNames.MongoDbTest, Type = TelemetryConstants.TopologyConstants.TypeDatabase, NodeCount = 1, TechBadge = TelemetryConstants.TopologyConstants.TechMongo, X = 430, Y = 60, CallsPerMin = 6, AvgLatencyMs = 15, ErrorRatePercent = 0.0 },
            new() { Id = TelemetryConstants.BackendNames.AppDyMySql, Label = TelemetryConstants.BackendNames.AppDyMySql, Type = TelemetryConstants.TopologyConstants.TypeDatabase, NodeCount = 1, TechBadge = TelemetryConstants.TopologyConstants.TechMySql, X = 740, Y = 30, CallsPerMin = 610, AvgLatencyMs = 3, ErrorRatePercent = 0.0 },

            // Queues
            new() { Id = TelemetryConstants.BackendNames.ActiveMqOrderQueue, Label = TelemetryConstants.BackendNames.ActiveMqOrderQueue, Type = TelemetryConstants.TopologyConstants.TypeQueue, NodeCount = 1, TechBadge = TelemetryConstants.TopologyConstants.TechActiveMq, X = 280, Y = 380, CallsPerMin = 23, AvgLatencyMs = 5, ErrorRatePercent = 0.0 },
            new() { Id = TelemetryConstants.BackendNames.ActiveMqFulfillmentQueue, Label = TelemetryConstants.BackendNames.ActiveMqFulfillmentQueue, Type = TelemetryConstants.TopologyConstants.TypeQueue, NodeCount = 1, TechBadge = TelemetryConstants.TopologyConstants.TechActiveMq, X = 660, Y = 460, CallsPerMin = 17, AvgLatencyMs = 4, ErrorRatePercent = 0.0 },
            new() { Id = TelemetryConstants.BackendNames.ActiveMqCustomerQueue, Label = TelemetryConstants.BackendNames.ActiveMqCustomerQueue, Type = TelemetryConstants.TopologyConstants.TypeQueue, NodeCount = 1, TechBadge = TelemetryConstants.TopologyConstants.TechActiveMq, X = 660, Y = 550, CallsPerMin = 6, AvgLatencyMs = 6, ErrorRatePercent = 0.0 }
        };

        // If order-service is active in DB, include it in the topology
        if (serviceAggregates.ContainsKey(TelemetryConstants.ServiceNames.DefaultOrderService))
        {
            var agg = serviceAggregates[TelemetryConstants.ServiceNames.DefaultOrderService];
            double cpm = Math.Round((double)agg.TotalCalls / minutes, 1);
            double errPct = agg.TotalCalls > 0 ? Math.Round((double)agg.ErrorCalls * 100.0 / agg.TotalCalls, 2) : 0.0;
            baseNodes.Add(new TopologyNodeDto
            {
                Id = TelemetryConstants.ServiceNames.DefaultOrderService,
                Label = TelemetryConstants.ServiceNames.DefaultOrderService,
                Type = TelemetryConstants.TopologyConstants.TypeService,
                NodeCount = 1,
                TechBadge = TelemetryConstants.TopologyConstants.TechDotNet,
                X = 160,
                Y = 400,
                CallsPerMin = Math.Max(cpm, 1.0),
                AvgLatencyMs = Math.Round(agg.AvgDuration, 1),
                ErrorRatePercent = errPct
            });
        }

        // Overlay live DB aggregates onto nodes
        foreach (var node in baseNodes)
        {
            if (serviceAggregates.TryGetValue(node.Id, out var agg) && agg.TotalCalls > 0)
            {
                node.CallsPerMin = Math.Round((double)agg.TotalCalls / minutes, 1);
                node.AvgLatencyMs = Math.Round(agg.AvgDuration, 1);
                node.ErrorRatePercent = Math.Round((double)agg.ErrorCalls * 100.0 / agg.TotalCalls, 2);
            }

            // Health calculation
            if (node.ErrorRatePercent > 5.0 || node.AvgLatencyMs > 600.0)
            {
                node.Health = TelemetryConstants.TopologyConstants.StatusCritical;
            }
            else if (node.ErrorRatePercent > 1.0 || node.AvgLatencyMs > 300.0)
            {
                node.Health = TelemetryConstants.TopologyConstants.StatusWarning;
            }
            else
            {
                node.Health = TelemetryConstants.TopologyConstants.StatusNormal;
            }
        }

        // 4. Define Base Edges matching AppDynamics topology
        var baseEdges = new List<TopologyEdgeDto>
        {
            new() { Id = "e1", Source = TelemetryConstants.ServiceNames.WebTierServices, Target = TelemetryConstants.ServiceNames.ECommerceServices, Protocol = TelemetryConstants.TopologyConstants.ProtocolHttp, CallsPerMin = 14, AvgLatencyMs = 95, IsAsync = false },
            new() { Id = "e2", Source = TelemetryConstants.ServiceNames.ECommerceServices, Target = TelemetryConstants.ServiceNames.InventoryServices, Protocol = TelemetryConstants.TopologyConstants.ProtocolHttp, CallsPerMin = 72, AvgLatencyMs = 18, IsAsync = false },
            new() { Id = "e3", Source = TelemetryConstants.ServiceNames.ECommerceServices, Target = TelemetryConstants.ServiceNames.AddressServices, Protocol = TelemetryConstants.TopologyConstants.ProtocolHttp, CallsPerMin = 192, AvgLatencyMs = 28, IsAsync = false },
            new() { Id = "e4", Source = TelemetryConstants.ServiceNames.ECommerceServices, Target = TelemetryConstants.BackendNames.OracleDbProduction, Protocol = TelemetryConstants.TopologyConstants.ProtocolJdbc, CallsPerMin = 11, AvgLatencyMs = 8, IsAsync = false },
            new() { Id = "e5", Source = TelemetryConstants.ServiceNames.ECommerceServices, Target = TelemetryConstants.BackendNames.AppDyMySql, Protocol = TelemetryConstants.TopologyConstants.ProtocolJdbc, CallsPerMin = 610, AvgLatencyMs = 3, IsAsync = false },
            new() { Id = "e6", Source = TelemetryConstants.ServiceNames.ECommerceServices, Target = TelemetryConstants.BackendNames.MongoDbTest, Protocol = TelemetryConstants.TopologyConstants.ProtocolMongo, CallsPerMin = 6, AvgLatencyMs = 15, IsAsync = false },
            new() { Id = "e7", Source = TelemetryConstants.ServiceNames.ECommerceServices, Target = TelemetryConstants.BackendNames.ActiveMqOrderQueue, Protocol = TelemetryConstants.TopologyConstants.ProtocolJms, CallsPerMin = 23, AvgLatencyMs = 5, IsAsync = true },
            new() { Id = "e8", Source = TelemetryConstants.ServiceNames.ECommerceServices, Target = TelemetryConstants.BackendNames.ActiveMqCustomerQueue, Protocol = TelemetryConstants.TopologyConstants.ProtocolJms, CallsPerMin = 6, AvgLatencyMs = 6, IsAsync = true },
            
            new() { Id = "e9", Source = TelemetryConstants.ServiceNames.InventoryServices, Target = TelemetryConstants.BackendNames.InventoryMySql, Protocol = TelemetryConstants.TopologyConstants.ProtocolJdbc, CallsPerMin = 435, AvgLatencyMs = 4, IsAsync = false },
            new() { Id = "e10", Source = TelemetryConstants.ServiceNames.InventoryServices, Target = TelemetryConstants.BackendNames.OracleDbProduction, Protocol = TelemetryConstants.TopologyConstants.ProtocolJdbc, CallsPerMin = 96, AvgLatencyMs = 9, IsAsync = false },
            
            new() { Id = "e11", Source = TelemetryConstants.BackendNames.ActiveMqOrderQueue, Target = TelemetryConstants.ServiceNames.OrderProcessingServices, Protocol = TelemetryConstants.TopologyConstants.ProtocolJms, CallsPerMin = 23, AvgLatencyMs = 4, IsAsync = true },
            new() { Id = "e12", Source = TelemetryConstants.ServiceNames.OrderProcessingServices, Target = TelemetryConstants.BackendNames.ActiveMqFulfillmentQueue, Protocol = TelemetryConstants.TopologyConstants.ProtocolJms, CallsPerMin = 17, AvgLatencyMs = 3, IsAsync = true },
            new() { Id = "e13", Source = TelemetryConstants.ServiceNames.OrderProcessingServices, Target = TelemetryConstants.ServiceNames.ECommerceServices, Protocol = TelemetryConstants.TopologyConstants.ProtocolHttp, CallsPerMin = 17, AvgLatencyMs = 35, IsAsync = false },
            
            new() { Id = "e14", Source = TelemetryConstants.BackendNames.ActiveMqCustomerQueue, Target = TelemetryConstants.ServiceNames.CustomerSurveyServices, Protocol = TelemetryConstants.TopologyConstants.ProtocolJms, CallsPerMin = 6, AvgLatencyMs = 5, IsAsync = true }
        };

        // If order-service exists, connect it to ECommerceServices
        if (serviceAggregates.ContainsKey(TelemetryConstants.ServiceNames.DefaultOrderService))
        {
            baseEdges.Add(new TopologyEdgeDto
            {
                Id = "e15",
                Source = TelemetryConstants.ServiceNames.DefaultOrderService,
                Target = TelemetryConstants.ServiceNames.ECommerceServices,
                Protocol = TelemetryConstants.TopologyConstants.ProtocolHttp,
                CallsPerMin = 12,
                AvgLatencyMs = 30,
                IsAsync = false
            });
        }

        // Overlay live trace edges
        foreach (var edge in baseEdges)
        {
            string key = $"{edge.Source}->{edge.Target}";
            if (edgeAggregates.TryGetValue(key, out var agg) && agg.TotalCalls > 0)
            {
                edge.CallsPerMin = Math.Round((double)agg.TotalCalls / minutes, 1);
                edge.AvgLatencyMs = Math.Round(agg.AvgDuration, 1);
                edge.ErrorRatePercent = Math.Round((double)agg.ErrorCalls * 100.0 / agg.TotalCalls, 2);
            }

            edge.Health = edge.ErrorRatePercent > 5.0 ? TelemetryConstants.TopologyConstants.StatusCritical
                        : edge.ErrorRatePercent > 1.0 ? TelemetryConstants.TopologyConstants.StatusWarning
                        : TelemetryConstants.TopologyConstants.StatusNormal;
        }

        // 5. Calculate Scorecard
        int nodesNormal = baseNodes.Count(n => n.Health == TelemetryConstants.TopologyConstants.StatusNormal);
        int nodesWarning = baseNodes.Count(n => n.Health == TelemetryConstants.TopologyConstants.StatusWarning);
        int nodesCritical = baseNodes.Count(n => n.Health == TelemetryConstants.TopologyConstants.StatusCritical);

        double totalCpm = Math.Round(baseNodes.Where(n => n.Type == TelemetryConstants.TopologyConstants.TypeService).Sum(n => n.CallsPerMin), 1);
        double avgLatency = Math.Round(baseNodes.Where(n => n.Type == TelemetryConstants.TopologyConstants.TypeService).Average(n => n.AvgLatencyMs), 1);
        long totalCalls = (long)(totalCpm * minutes);
        long totalErrors = (long)(baseNodes.Where(n => n.Type == TelemetryConstants.TopologyConstants.TypeService).Sum(n => n.CallsPerMin * (n.ErrorRatePercent / 100.0)) * minutes);
        double errorsPerMin = minutes > 0 ? Math.Round((double)totalErrors / minutes, 2) : 0;

        var scorecard = new TopologyScorecardDto
        {
            NormalPercent = 98.4,
            SlowPercent = 1.1,
            VerySlowPercent = 0.3,
            StallPercent = 0.0,
            ErrorPercent = 0.2,
            NodesNormal = nodesNormal,
            NodesWarning = nodesWarning,
            NodesCritical = nodesCritical,
            TotalCalls = Math.Max(totalCalls, 45000),
            CallsPerMin = totalCpm > 0 ? totalCpm : 865.0,
            AvgLatencyMs = avgLatency > 0 ? avgLatency : 42.0,
            TotalExceptions = Math.Max(totalErrors, 12),
            ErrorsPerMin = errorsPerMin > 0 ? errorsPerMin : 0.8
        };

        // 6. Generate Time Series for bottom ribbon charts
        var timeSeries = new List<TopologyTimeSeriesPoint>();
        int steps = Math.Min(minutes, 30);
        if (steps < 10) steps = 10;
        var stepIntervalSeconds = (minutes * 60) / steps;
        var now = DateTime.UtcNow;

        var rnd = new Random(TelemetryConstants.SeedDefaults.RandomSeed + minutes);
        for (int i = steps; i >= 0; i--)
        {
            var pointTime = now.AddSeconds(-i * stepIntervalSeconds);
            double jitter = (rnd.NextDouble() - 0.5) * 0.15; // +/- 7.5%
            double cpm = Math.Round(scorecard.CallsPerMin * (1.0 + jitter), 1);
            double lat = Math.Round(scorecard.AvgLatencyMs * (1.0 + (rnd.NextDouble() - 0.4) * 0.2), 1);
            double errPct = Math.Max(0.05, Math.Round(scorecard.ErrorPercent * (1.0 + (rnd.NextDouble() - 0.5) * 0.4), 2));

            timeSeries.Add(new TopologyTimeSeriesPoint
            {
                Timestamp = pointTime.ToString("o"),
                CallsPerMin = cpm,
                AvgLatencyMs = lat,
                ErrorRatePercent = errPct
            });
        }

        var response = new TopologyFlowMapResponse
        {
            Nodes = baseNodes,
            Edges = baseEdges,
            Scorecard = scorecard,
            TimeSeries = timeSeries,
            ApplicationName = application,
            WindowMinutes = minutes
        };

        return Ok(response);
    }

    [HttpGet("nodes/{nodeId}")]
    public async Task<IActionResult> GetNodeDetails(
        string nodeId,
        [FromQuery] int minutes = AppConstants.QueryDefaults.DefaultMetricsRecentMinutes)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return BadRequest(new { error = "Node ID is required." });

        if (minutes <= 0) minutes = AppConstants.QueryDefaults.DefaultMetricsRecentMinutes;
        var windowStart = DateTime.UtcNow.AddMinutes(-minutes).ToString("o");

        using var conn = new SqliteConnection(_dbConn);
        await conn.OpenAsync();

        // Query node traces
        var traces = await conn.QueryAsync<TraceSpanDto>(
            @"SELECT TraceId, SpanId, ParentSpanId, ServiceName, SpanName, DurationMs, StatusCode, Timestamp 
              FROM Traces 
              WHERE ServiceName = @nodeId AND Timestamp >= @windowStart
              ORDER BY Timestamp DESC LIMIT 20;",
            new { nodeId, windowStart }
        );

        // Query node logs
        var logs = await conn.QueryAsync<LogRecordDto>(
            @"SELECT Id, Timestamp, TraceId, SpanId, ServiceName, SeverityText, SeverityNumber, Body 
              FROM Logs 
              WHERE ServiceName = @nodeId AND Timestamp >= @windowStart
              ORDER BY Timestamp DESC LIMIT 20;",
            new { nodeId, windowStart }
        );

        return Ok(new
        {
            NodeId = nodeId,
            WindowMinutes = minutes,
            TraceCount = traces.Count(),
            LogCount = logs.Count(),
            RecentTraces = traces,
            RecentLogs = logs
        });
    }
}

using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using KestrelScope.Constants;
using KestrelScope.Models;

namespace KestrelScope;

public static class DbInitializer
{
    public static void Initialize(string connectionString)
    {
        EnsureDatabaseDirectory(connectionString);

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        ConfigureWalPragma(connection);
        InitializeSchema(connection);
        MigrateUsersRoleColumn(connection);
        EnsureAdminUser(connection);
        SeedBaselineMetrics(connection);
        SeedBaselineTraces(connection);
        SeedBaselineLogs(connection);
        SeedDefaultSettings(connection);

        Console.WriteLine("[KestrelScope] SQLite database initialized successfully in WAL mode.");
    }

    private static void EnsureDatabaseDirectory(string connectionString)
    {
        var csBuilder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(csBuilder.DataSource)) return;

        var dir = Path.GetDirectoryName(csBuilder.DataSource);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static void ConfigureWalPragma(SqliteConnection connection)
    {
        using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = AppConstants.Database.WalPragmaSettings;
        pragmaCmd.ExecuteNonQuery();
    }

    private static void InitializeSchema(SqliteConnection connection)
    {
        string schemaSql = $@"
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT UNIQUE NOT NULL,
                PasswordHash TEXT NOT NULL,
                Role TEXT NOT NULL DEFAULT '{AppConstants.UserRoles.Standard}',
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

            CREATE TABLE IF NOT EXISTS Logs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp DATETIME NOT NULL,
                TraceId TEXT,
                SpanId TEXT,
                ServiceName TEXT NOT NULL,
                SeverityText TEXT NOT NULL,
                SeverityNumber INTEGER NOT NULL,
                Body TEXT NOT NULL,
                AttributesJson TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_logs_lookup ON Logs(ServiceName, Timestamp);
            CREATE INDEX IF NOT EXISTS idx_logs_trace ON Logs(TraceId);

            CREATE TABLE IF NOT EXISTS DatabaseSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                UpdatedBy TEXT
            );

            CREATE TABLE IF NOT EXISTS AdminAuditLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                UserId TEXT,
                Username TEXT NOT NULL,
                Action TEXT NOT NULL,
                Target TEXT,
                DetailsJson TEXT,
                IpAddress TEXT,
                Status TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_audit_timestamp ON AdminAuditLogs(Timestamp);
            CREATE INDEX IF NOT EXISTS idx_audit_action ON AdminAuditLogs(Action);
        ";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = schemaSql;
        cmd.ExecuteNonQuery();
    }

    private static void MigrateUsersRoleColumn(SqliteConnection connection)
    {
        try
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = $"ALTER TABLE Users ADD COLUMN Role TEXT NOT NULL DEFAULT '{AppConstants.UserRoles.Standard}';";
            alterCmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Column already exists, safe to ignore
        }
    }

    private static void EnsureAdminUser(SqliteConnection connection)
    {
        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM Users;";
        long count = (long)(countCmd.ExecuteScalar() ?? 0L);
        if (count == 0)
        {
            using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO Users (Username, PasswordHash, Role) 
                VALUES (@username, @passwordHash, @role);";
            insertCmd.Parameters.AddWithValue("@username", AppConstants.UserRoles.Admin);
            insertCmd.Parameters.AddWithValue("@passwordHash", Services.PasswordHasher.HashPassword(AppConstants.UserRoles.Admin));
            insertCmd.Parameters.AddWithValue("@role", AppConstants.UserRoles.Admin);
            insertCmd.ExecuteNonQuery();
            Console.WriteLine($"[KestrelScope] Seeded default administrator user: '{AppConstants.UserRoles.Admin}'");
            return;
        }

        // Ensure default admin user has admin role if previously seeded
        using var ensureAdminRoleCmd = connection.CreateCommand();
        ensureAdminRoleCmd.CommandText = $"UPDATE Users SET Role = '{AppConstants.UserRoles.Admin}' WHERE Username = '{AppConstants.UserRoles.Admin}' AND (Role IS NULL OR Role = '' OR Role = '{AppConstants.UserRoles.Standard}');";
        ensureAdminRoleCmd.ExecuteNonQuery();
    }

    private static void SeedBaselineMetrics(SqliteConnection connection)
    {
        using var metricsCountCmd = connection.CreateCommand();
        metricsCountCmd.CommandText = "SELECT COUNT(*) FROM MetricSamples WHERE Timestamp >= @since;";
        metricsCountCmd.Parameters.AddWithValue("@since", DateTime.UtcNow.AddMinutes(-TelemetryConstants.SeedDefaults.MetricsRecentCheckMinutes).ToString("o"));
        long count = (long)(metricsCountCmd.ExecuteScalar() ?? 0L);
        if (count > 0) return;

        var rand = new Random(TelemetryConstants.SeedDefaults.RandomSeed);
        var now = DateTime.UtcNow;

        var minuteOffsets = GenerateMetricMinuteOffsets();

        using var trans = connection.BeginTransaction();
        using var insertSampleCmd = connection.CreateCommand();
        insertSampleCmd.Transaction = trans;
        insertSampleCmd.CommandText = @"
            INSERT INTO MetricSamples (ServiceName, MetricName, Value, Timestamp)
            VALUES (@service, @metric, @val, @time);";

        var pService = insertSampleCmd.Parameters.Add("@service", SqliteType.Text);
        var pMetric = insertSampleCmd.Parameters.Add("@metric", SqliteType.Text);
        var pVal = insertSampleCmd.Parameters.Add("@val", SqliteType.Real);
        var pTime = insertSampleCmd.Parameters.Add("@time", SqliteType.Text);

        void InsertMetric(string service, string metric, double val, string time)
        {
            pService.Value = service;
            pMetric.Value = metric;
            pVal.Value = val;
            pTime.Value = time;
            insertSampleCmd.ExecuteNonQuery();
        }

        foreach (int off in minuteOffsets)
        {
            var sampleTime = now.AddMinutes(-off).ToString("o");
            double latencyVal = 65 + 30 * Math.Sin(off / 30.0) + rand.NextDouble() * 25;
            double ordersVal = 1 + rand.Next(0, 3);
            double memoryVal = 185 + 25 * Math.Cos(off / 60.0) + rand.NextDouble() * 15;
            double failedOrdersVal = (off % 12 == 0) ? 1.0 : 0.0;

            InsertMetric(TelemetryConstants.ServiceNames.DefaultOrderService, TelemetryConstants.MetricNames.HttpServerRequestDuration, Math.Round(latencyVal, 2), sampleTime);
            InsertMetric(TelemetryConstants.ServiceNames.DefaultOrderService, TelemetryConstants.MetricNames.OrdersCreatedCount, ordersVal, sampleTime);
            InsertMetric(TelemetryConstants.ServiceNames.DefaultOrderService, TelemetryConstants.MetricNames.OrdersFailedCount, failedOrdersVal, sampleTime);
            InsertMetric(TelemetryConstants.ServiceNames.DefaultOrderService, TelemetryConstants.MetricNames.ProcessMemoryUsage, Math.Round(memoryVal, 2), sampleTime);
        }

        // Multi-service metrics for topology nodes
        var otherServices = new (string Name, double LatencyBase, double MemoryBase)[]
        {
            (TelemetryConstants.ServiceNames.ECommerceServices, 42.0, 512.0),
            (TelemetryConstants.ServiceNames.InventoryServices, 18.0, 256.0),
            (TelemetryConstants.ServiceNames.AddressServices, 28.0, 192.0),
            (TelemetryConstants.ServiceNames.WebTierServices, 95.0, 384.0),
            (TelemetryConstants.ServiceNames.OrderProcessingServices, 85.0, 320.0),
            (TelemetryConstants.ServiceNames.CustomerSurveyServices, 120.0, 160.0)
        };

        foreach (var svc in otherServices)
        {
            foreach (int off in minuteOffsets)
            {
                var sampleTime = now.AddMinutes(-off).ToString("o");
                double lat = svc.LatencyBase + 10 * Math.Sin(off / 20.0) + rand.NextDouble() * 5;
                double mem = svc.MemoryBase + 20 * Math.Cos(off / 40.0) + rand.NextDouble() * 10;

                InsertMetric(svc.Name, TelemetryConstants.MetricNames.HttpServerRequestDuration, Math.Round(lat, 2), sampleTime);
                InsertMetric(svc.Name, TelemetryConstants.MetricNames.ProcessMemoryUsage, Math.Round(mem, 2), sampleTime);
            }
        }

        trans.Commit();
        Console.WriteLine("[KestrelScope] Seeded baseline telemetry samples spanning 24 hours across multi-service topology.");
    }

    private static List<int> GenerateMetricMinuteOffsets()
    {
        var minuteOffsets = new List<int>();
        for (int m = 0; m <= 60; m++)
        {
            if (m <= 5)
            {
                if (m % 2 == 0) minuteOffsets.Add(m);
            }
            else if (m <= 20)
            {
                if (m % 2 == 0) minuteOffsets.Add(m);
            }
            else if (m % 5 == 0)
            {
                minuteOffsets.Add(m);
            }
        }
        for (int m = 75; m <= 360; m += 15) minuteOffsets.Add(m);
        for (int m = 405; m <= TelemetryConstants.SeedDefaults.MaxHistoryMinutes; m += 45) minuteOffsets.Add(m);
        return minuteOffsets;
    }

    private static void SeedBaselineTraces(SqliteConnection connection)
    {
        using var tracesCountCmd = connection.CreateCommand();
        tracesCountCmd.CommandText = "SELECT COUNT(*) FROM Traces WHERE Timestamp >= @since;";
        tracesCountCmd.Parameters.AddWithValue("@since", DateTime.UtcNow.AddMinutes(-TelemetryConstants.SeedDefaults.TracesRecentCheckMinutes).ToString("o"));
        long count = (long)(tracesCountCmd.ExecuteScalar() ?? 0L);
        if (count > 0) return;

        var now = DateTime.UtcNow;
        using var trans = connection.BeginTransaction();
        using var insertTraceCmd = connection.CreateCommand();
        insertTraceCmd.Transaction = trans;
        insertTraceCmd.CommandText = @"
            INSERT INTO Traces (TraceId, SpanId, ParentSpanId, ServiceName, SpanName, DurationMs, StatusCode, Timestamp)
            VALUES (@traceId, @spanId, @parentSpanId, @service, @spanName, @duration, @status, @time);";

        var pTrace = insertTraceCmd.Parameters.Add("@traceId", SqliteType.Text);
        var pSpan = insertTraceCmd.Parameters.Add("@spanId", SqliteType.Text);
        var pParent = insertTraceCmd.Parameters.Add("@parentSpanId", SqliteType.Text);
        var pService = insertTraceCmd.Parameters.Add("@service", SqliteType.Text);
        var pName = insertTraceCmd.Parameters.Add("@spanName", SqliteType.Text);
        var pDuration = insertTraceCmd.Parameters.Add("@duration", SqliteType.Real);
        var pStatus = insertTraceCmd.Parameters.Add("@status", SqliteType.Text);
        var pTime = insertTraceCmd.Parameters.Add("@time", SqliteType.Text);

        void InsertSpan(string traceId, string spanId, string? parentSpanId, string service, string spanName, double duration, string status, string time)
        {
            pTrace.Value = traceId;
            pSpan.Value = spanId;
            pParent.Value = (object?)parentSpanId ?? DBNull.Value;
            pService.Value = service;
            pName.Value = spanName;
            pDuration.Value = duration;
            pStatus.Value = status;
            pTime.Value = time;
            insertTraceCmd.ExecuteNonQuery();
        }

        string okStatus = TelemetryConstants.SpanStatusNames.Ok;
        string errorStatus = TelemetryConstants.SpanStatusNames.Error;

        int[] traceOffsets = [28, 20, 14, 8, 3, 1];
        foreach (int off in traceOffsets)
        {
            string tId = Guid.NewGuid().ToString("N");
            string rootId = Guid.NewGuid().ToString("N")[..16];
            string invId = Guid.NewGuid().ToString("N")[..16];
            string payId = Guid.NewGuid().ToString("N")[..16];
            string dbId = Guid.NewGuid().ToString("N")[..16];
            string time = now.AddMinutes(-off).ToString("o");

            InsertSpan(tId, rootId, null, TelemetryConstants.ServiceNames.DefaultOrderService, TelemetryConstants.SpanNames.CreateOrder, 78.5, okStatus, time);
            InsertSpan(tId, invId, rootId, TelemetryConstants.ServiceNames.DefaultOrderService, TelemetryConstants.SpanNames.ValidateStock, 16.0, okStatus, time);
            InsertSpan(tId, payId, rootId, TelemetryConstants.ServiceNames.DefaultOrderService, TelemetryConstants.SpanNames.ChargeCard, 39.5, okStatus, time);
            InsertSpan(tId, dbId, rootId, TelemetryConstants.ServiceNames.DefaultOrderService, TelemetryConstants.SpanNames.SaveOrder, 20.0, okStatus, time);
        }

        int[] multiTraceOffsets = [25, 18, 12, 6, 2];
        foreach (int off in multiTraceOffsets)
        {
            bool isErr = (off == 12 || off == 2);
            string status = isErr ? errorStatus : okStatus;
            string tId = Guid.NewGuid().ToString("N");
            string webId = Guid.NewGuid().ToString("N")[..16];
            string ecomId = Guid.NewGuid().ToString("N")[..16];
            string invId = Guid.NewGuid().ToString("N")[..16];
            string addrId = Guid.NewGuid().ToString("N")[..16];
            string orderProcId = Guid.NewGuid().ToString("N")[..16];
            string surveyId = Guid.NewGuid().ToString("N")[..16];
            string time = now.AddMinutes(-off).ToString("o");

            InsertSpan(tId, webId, null, TelemetryConstants.ServiceNames.WebTierServices, "GET /checkout", 95.0, status, time);
            InsertSpan(tId, ecomId, webId, TelemetryConstants.ServiceNames.ECommerceServices, "POST /api/cart/process", isErr ? 142.0 : 42.0, status, time);
            InsertSpan(tId, invId, ecomId, TelemetryConstants.ServiceNames.InventoryServices, "InventoryService.ValidateStock", 18.0, okStatus, time);
            InsertSpan(tId, addrId, ecomId, TelemetryConstants.ServiceNames.AddressServices, "AddressService.ValidateShipping", 28.0, okStatus, time);
            InsertSpan(tId, orderProcId, ecomId, TelemetryConstants.ServiceNames.OrderProcessingServices, "OrderProcessor.HandleQueue", isErr ? 185.0 : 85.0, status, time);
            InsertSpan(tId, surveyId, orderProcId, TelemetryConstants.ServiceNames.CustomerSurveyServices, "SurveyService.ScheduleSurvey", 120.0, okStatus, time);
        }

        trans.Commit();
        Console.WriteLine("[KestrelScope] Seeded baseline trace spans across multi-service topology.");
    }

    private static void SeedBaselineLogs(SqliteConnection connection)
    {
        using var logsCountCmd = connection.CreateCommand();
        logsCountCmd.CommandText = "SELECT COUNT(*) FROM Logs WHERE Timestamp >= @since;";
        logsCountCmd.Parameters.AddWithValue("@since", DateTime.UtcNow.AddMinutes(-TelemetryConstants.SeedDefaults.LogsRecentCheckMinutes).ToString("o"));
        long count = (long)(logsCountCmd.ExecuteScalar() ?? 0L);
        if (count > 0) return;

        using var insertLogCmd = connection.CreateCommand();
        insertLogCmd.CommandText = @"
            INSERT INTO Logs (Timestamp, TraceId, SpanId, ServiceName, SeverityText, SeverityNumber, Body, AttributesJson)
            VALUES (@time, @traceId, @spanId, @service, @sevText, @sevNum, @body, @attrs);";

        var pTime = insertLogCmd.Parameters.Add("@time", SqliteType.Text);
        var pTraceId = insertLogCmd.Parameters.Add("@traceId", SqliteType.Text);
        var pSpanId = insertLogCmd.Parameters.Add("@spanId", SqliteType.Text);
        var pService = insertLogCmd.Parameters.Add("@service", SqliteType.Text);
        var pSevText = insertLogCmd.Parameters.Add("@sevText", SqliteType.Text);
        var pSevNum = insertLogCmd.Parameters.Add("@sevNum", SqliteType.Integer);
        var pBody = insertLogCmd.Parameters.Add("@body", SqliteType.Text);
        var pAttrs = insertLogCmd.Parameters.Add("@attrs", SqliteType.Text);

        pAttrs.Value = "{}";

        void InsertLog(string service, string time, string sevText, int sevNum, string body, string? traceId = null, string? spanId = null)
        {
            pService.Value = service;
            pTime.Value = time;
            pSevText.Value = sevText;
            pSevNum.Value = sevNum;
            pBody.Value = body;
            pTraceId.Value = (object?)traceId ?? DBNull.Value;
            pSpanId.Value = (object?)spanId ?? DBNull.Value;
            insertLogCmd.ExecuteNonQuery();
        }

        var now = DateTime.UtcNow;
        var baselineLogs = new (int offsetMin, string sevText, int sevNum, string body, string? trace, string? span)[]
        {
            (45, TelemetryConstants.SeverityText.Info, (int)OtelSeverity.Info, "Application host started successfully in production environment.", null, null),
            (30, TelemetryConstants.SeverityText.Info, (int)OtelSeverity.Info, "Connection pool initialized to internal storage engine.", null, null),
            (20, TelemetryConstants.SeverityText.Info, (int)OtelSeverity.Info, $"Distributed tracing tracer provider registered for {TelemetryConstants.ServiceNames.DefaultOrderService}.", null, null),
            (15, TelemetryConstants.SeverityText.Warn, (int)OtelSeverity.Warn, "Degraded query performance detected on cold cache access.", null, null),
            (8, TelemetryConstants.SeverityText.Info, (int)OtelSeverity.Info, $"Batch telemetry sync completed for {TelemetryConstants.ServiceNames.DefaultOrderService}.", null, null),
            (3, TelemetryConstants.SeverityText.Info, (int)OtelSeverity.Info, "System health check status reported OK.", null, null),
            (1, TelemetryConstants.SeverityText.Info, (int)OtelSeverity.Info, "Periodic order queue synchronization completed successfully.", null, null)
        };

        foreach (var log in baselineLogs)
        {
            InsertLog(TelemetryConstants.ServiceNames.DefaultOrderService, now.AddMinutes(-log.offsetMin).ToString("o"), log.sevText, log.sevNum, log.body, log.trace, log.span);
        }

        var multiServiceLogs = new (string svc, int offsetMin, string sevText, int sevNum, string body)[]
        {
            (TelemetryConstants.ServiceNames.ECommerceServices, 25, TelemetryConstants.SeverityText.Info, (int)OtelSeverity.Info, "ECommerce cluster node 1 active; transaction pool synchronized."),
            (TelemetryConstants.ServiceNames.ECommerceServices, 2, TelemetryConstants.SeverityText.Error, (int)OtelSeverity.Error, "Transaction timeout handling payment gateway callback for checkout."),
            (TelemetryConstants.ServiceNames.InventoryServices, 20, TelemetryConstants.SeverityText.Info, (int)OtelSeverity.Info, "Inventory-MySQL master read replica healthy. Cache hit ratio 99.1%."),
            (TelemetryConstants.ServiceNames.AddressServices, 18, TelemetryConstants.SeverityText.Info, (int)OtelSeverity.Info, "Postal code validation cache warmed."),
            (TelemetryConstants.ServiceNames.OrderProcessingServices, 14, TelemetryConstants.SeverityText.Info, (int)OtelSeverity.Info, "ActiveMQ-OrderQueue listener connected on channel 1."),
            (TelemetryConstants.ServiceNames.OrderProcessingServices, 12, TelemetryConstants.SeverityText.Warn, (int)OtelSeverity.Warn, "High queue consumer lag detected on ActiveMQ-OrderQueue channel."),
            (TelemetryConstants.ServiceNames.CustomerSurveyServices, 10, TelemetryConstants.SeverityText.Info, (int)OtelSeverity.Info, "Survey dispatch batch queue idle.")
        };

        foreach (var log in multiServiceLogs)
        {
            InsertLog(log.svc, now.AddMinutes(-log.offsetMin).ToString("o"), log.sevText, log.sevNum, log.body);
        }

        Console.WriteLine("[KestrelScope] Seeded baseline log records.");
    }

    private static void SeedDefaultSettings(SqliteConnection connection)
    {
        var defaultSettings = new Dictionary<string, string>
        {
            ["RetentionMetricsDays"] = AppConstants.DatabaseManagement.DefaultRetentionMetricsDays.ToString(),
            ["RetentionTracesDays"] = AppConstants.DatabaseManagement.DefaultRetentionTracesDays.ToString(),
            ["RetentionLogsDays"] = AppConstants.DatabaseManagement.DefaultRetentionLogsDays.ToString(),
            ["RetentionAlertsDays"] = AppConstants.DatabaseManagement.DefaultRetentionAlertsDays.ToString(),
            ["RetentionAuditLogsDays"] = AppConstants.DatabaseManagement.DefaultRetentionAuditLogsDays.ToString(),
            ["AutoPruneEnabled"] = AppConstants.DatabaseManagement.DefaultAutoPruneEnabled.ToString().ToLowerInvariant(),
            ["AutoPruneHourUtc"] = AppConstants.DatabaseManagement.DefaultAutoPruneHourUtc.ToString(),
            ["AutoBackupEnabled"] = AppConstants.DatabaseManagement.DefaultAutoBackupEnabled.ToString().ToLowerInvariant(),
            ["AutoBackupHourUtc"] = AppConstants.DatabaseManagement.DefaultAutoBackupHourUtc.ToString(),
            ["BackupRetentionCount"] = AppConstants.DatabaseManagement.DefaultBackupRetentionCount.ToString(),
            ["StorageWarningThresholdMb"] = AppConstants.DatabaseManagement.DefaultStorageWarningThresholdMb.ToString()
        };

        using var settingCmd = connection.CreateCommand();
        settingCmd.CommandText = @"
            INSERT OR IGNORE INTO DatabaseSettings (Key, Value, UpdatedAt, UpdatedBy)
            VALUES (@key, @value, CURRENT_TIMESTAMP, 'System');";
        var pKey = settingCmd.Parameters.Add("@key", SqliteType.Text);
        var pValue = settingCmd.Parameters.Add("@value", SqliteType.Text);

        foreach (var kvp in defaultSettings)
        {
            pKey.Value = kvp.Key;
            pValue.Value = kvp.Value;
            settingCmd.ExecuteNonQuery();
        }
    }
}

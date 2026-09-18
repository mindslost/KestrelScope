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
        var csBuilder = new SqliteConnectionStringBuilder(connectionString);
        if (!string.IsNullOrWhiteSpace(csBuilder.DataSource))
        {
            var dir = Path.GetDirectoryName(csBuilder.DataSource);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using (var pragmaCmd = connection.CreateCommand())
        {
            pragmaCmd.CommandText = AppConstants.Database.WalPragmaSettings;
            pragmaCmd.ExecuteNonQuery();
        }

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

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = schemaSql;
            cmd.ExecuteNonQuery();
        }

        // Migrate existing Users table if Role column is missing
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

        // Seed default admin user if Users table is empty
        using (var countCmd = connection.CreateCommand())
        {
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
            }
            else
            {
                // Ensure default admin user has admin role if previously seeded
                using var ensureAdminRoleCmd = connection.CreateCommand();
                ensureAdminRoleCmd.CommandText = $"UPDATE Users SET Role = '{AppConstants.UserRoles.Admin}' WHERE Username = '{AppConstants.UserRoles.Admin}' AND (Role IS NULL OR Role = '' OR Role = '{AppConstants.UserRoles.Standard}');";
                ensureAdminRoleCmd.ExecuteNonQuery();
            }
        }

        // Seed baseline telemetry if no samples exist within the last 15 minutes
        using (var metricsCountCmd = connection.CreateCommand())
        {
            metricsCountCmd.CommandText = "SELECT COUNT(*) FROM MetricSamples WHERE Timestamp >= @since;";
            metricsCountCmd.Parameters.AddWithValue("@since", DateTime.UtcNow.AddMinutes(-TelemetryConstants.SeedDefaults.MetricsRecentCheckMinutes).ToString("o"));
            long count = (long)(metricsCountCmd.ExecuteScalar() ?? 0L);
            if (count == 0)
            {
                var rand = new Random(TelemetryConstants.SeedDefaults.RandomSeed);
                var now = DateTime.UtcNow;

                var minuteOffsets = new List<int>();
                // Last 1 hour: dense sampling across the entire 60 minutes
                // (Offsets 4, 2, 0 in the last 5 minutes to keep alert threshold testing robust)
                for (int m = 0; m <= 60; m++)
                {
                    if (m <= 5)
                    {
                        if (m % 2 == 0) minuteOffsets.Add(m); // 0, 2, 4
                    }
                    else if (m <= 20)
                    {
                        if (m % 2 == 0) minuteOffsets.Add(m); // 6, 8, 10, 12, 14, 16, 18, 20
                    }
                    else
                    {
                        if (m % 5 == 0) minuteOffsets.Add(m); // 25, 30, 35, 40, 45, 50, 55, 60
                    }
                }
                // 1 hour to 6 hours
                for (int m = 75; m <= 360; m += 15) minuteOffsets.Add(m);
                // 6 hours to 24 hours
                for (int m = 405; m <= TelemetryConstants.SeedDefaults.MaxHistoryMinutes; m += 45) minuteOffsets.Add(m);

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

                pService.Value = TelemetryConstants.ServiceNames.DefaultOrderService;

                foreach (int off in minuteOffsets)
                {
                    var sampleTime = now.AddMinutes(-off).ToString("o");
                    double latencyVal = 65 + 30 * Math.Sin(off / 30.0) + rand.NextDouble() * 25;
                    double ordersVal = 1 + rand.Next(0, 3);
                    double memoryVal = 185 + 25 * Math.Cos(off / 60.0) + rand.NextDouble() * 15;
                    double failedOrdersVal = (off % 12 == 0) ? 1.0 : 0.0;

                    pMetric.Value = TelemetryConstants.MetricNames.HttpServerRequestDuration;
                    pVal.Value = Math.Round(latencyVal, 2);
                    pTime.Value = sampleTime;
                    insertSampleCmd.ExecuteNonQuery();

                    pMetric.Value = TelemetryConstants.MetricNames.OrdersCreatedCount;
                    pVal.Value = ordersVal;
                    pTime.Value = sampleTime;
                    insertSampleCmd.ExecuteNonQuery();

                    pMetric.Value = TelemetryConstants.MetricNames.OrdersFailedCount;
                    pVal.Value = failedOrdersVal;
                    pTime.Value = sampleTime;
                    insertSampleCmd.ExecuteNonQuery();

                    pMetric.Value = TelemetryConstants.MetricNames.ProcessMemoryUsage;
                    pVal.Value = Math.Round(memoryVal, 2);
                    pTime.Value = sampleTime;
                    insertSampleCmd.ExecuteNonQuery();
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
                    pService.Value = svc.Name;
                    foreach (int off in minuteOffsets)
                    {
                        var sampleTime = now.AddMinutes(-off).ToString("o");
                        double lat = svc.LatencyBase + 10 * Math.Sin(off / 20.0) + rand.NextDouble() * 5;
                        double mem = svc.MemoryBase + 20 * Math.Cos(off / 40.0) + rand.NextDouble() * 10;

                        pMetric.Value = TelemetryConstants.MetricNames.HttpServerRequestDuration;
                        pVal.Value = Math.Round(lat, 2);
                        pTime.Value = sampleTime;
                        insertSampleCmd.ExecuteNonQuery();

                        pMetric.Value = TelemetryConstants.MetricNames.ProcessMemoryUsage;
                        pVal.Value = Math.Round(mem, 2);
                        pTime.Value = sampleTime;
                        insertSampleCmd.ExecuteNonQuery();
                    }
                }

                trans.Commit();
                Console.WriteLine($"[KestrelScope] Seeded baseline telemetry samples spanning 24 hours across multi-service topology.");
            }
        }

        // Seed baseline traces if Traces table has no recent spans in the last 30 minutes
        using (var tracesCountCmd = connection.CreateCommand())
        {
            tracesCountCmd.CommandText = "SELECT COUNT(*) FROM Traces WHERE Timestamp >= @since;";
            tracesCountCmd.Parameters.AddWithValue("@since", DateTime.UtcNow.AddMinutes(-TelemetryConstants.SeedDefaults.TracesRecentCheckMinutes).ToString("o"));
            long count = (long)(tracesCountCmd.ExecuteScalar() ?? 0L);
            if (count == 0)
            {
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

                string okStatusCode = TelemetryConstants.SpanStatusNames.Ok;
                string errorStatusCode = TelemetryConstants.SpanStatusNames.Error;
                int[] traceOffsets = [28, 20, 14, 8, 3, 1];
                foreach (int off in traceOffsets)
                {
                    string tId = Guid.NewGuid().ToString("N");
                    string rootId = Guid.NewGuid().ToString("N")[..16];
                    string invId = Guid.NewGuid().ToString("N")[..16];
                    string payId = Guid.NewGuid().ToString("N")[..16];
                    string dbId = Guid.NewGuid().ToString("N")[..16];
                    string time = now.AddMinutes(-off).ToString("o");

                    // Root span
                    pTrace.Value = tId; pSpan.Value = rootId; pParent.Value = DBNull.Value;
                    pService.Value = TelemetryConstants.ServiceNames.DefaultOrderService; pName.Value = TelemetryConstants.SpanNames.CreateOrder;
                    pDuration.Value = 78.5; pStatus.Value = okStatusCode; pTime.Value = time;
                    insertTraceCmd.ExecuteNonQuery();

                    // Inventory step
                    pSpan.Value = invId; pParent.Value = rootId; pName.Value = TelemetryConstants.SpanNames.ValidateStock;
                    pDuration.Value = 16.0; pStatus.Value = okStatusCode;
                    insertTraceCmd.ExecuteNonQuery();

                    // Payment step
                    pSpan.Value = payId; pParent.Value = rootId; pName.Value = TelemetryConstants.SpanNames.ChargeCard;
                    pDuration.Value = 39.5; pStatus.Value = okStatusCode;
                    insertTraceCmd.ExecuteNonQuery();

                    // DB step
                    pSpan.Value = dbId; pParent.Value = rootId; pName.Value = TelemetryConstants.SpanNames.SaveOrder;
                    pDuration.Value = 20.0; pStatus.Value = okStatusCode;
                    insertTraceCmd.ExecuteNonQuery();
                }

                // Multi-service distributed trace journeys
                int[] multiTraceOffsets = [25, 18, 12, 6, 2];
                foreach (int off in multiTraceOffsets)
                {
                    bool isErr = (off == 12 || off == 2);
                    string tId = Guid.NewGuid().ToString("N");
                    string webId = Guid.NewGuid().ToString("N")[..16];
                    string ecomId = Guid.NewGuid().ToString("N")[..16];
                    string invId = Guid.NewGuid().ToString("N")[..16];
                    string addrId = Guid.NewGuid().ToString("N")[..16];
                    string orderProcId = Guid.NewGuid().ToString("N")[..16];
                    string surveyId = Guid.NewGuid().ToString("N")[..16];
                    string time = now.AddMinutes(-off).ToString("o");

                    // 1. Web Tier (Root)
                    pTrace.Value = tId; pSpan.Value = webId; pParent.Value = DBNull.Value;
                    pService.Value = TelemetryConstants.ServiceNames.WebTierServices; pName.Value = "GET /checkout";
                    pDuration.Value = 95.0; pStatus.Value = isErr ? errorStatusCode : okStatusCode; pTime.Value = time;
                    insertTraceCmd.ExecuteNonQuery();

                    // 2. ECommerce Services (Child of Web Tier)
                    pSpan.Value = ecomId; pParent.Value = webId;
                    pService.Value = TelemetryConstants.ServiceNames.ECommerceServices; pName.Value = "POST /api/cart/process";
                    pDuration.Value = isErr ? 142.0 : 42.0; pStatus.Value = isErr ? errorStatusCode : okStatusCode;
                    insertTraceCmd.ExecuteNonQuery();

                    // 3. Inventory Services (Child of ECommerce)
                    pSpan.Value = invId; pParent.Value = ecomId;
                    pService.Value = TelemetryConstants.ServiceNames.InventoryServices; pName.Value = "InventoryService.ValidateStock";
                    pDuration.Value = 18.0; pStatus.Value = okStatusCode;
                    insertTraceCmd.ExecuteNonQuery();

                    // 4. Address Services (Child of ECommerce)
                    pSpan.Value = addrId; pParent.Value = ecomId;
                    pService.Value = TelemetryConstants.ServiceNames.AddressServices; pName.Value = "AddressService.ValidateShipping";
                    pDuration.Value = 28.0; pStatus.Value = okStatusCode;
                    insertTraceCmd.ExecuteNonQuery();

                    // 5. Order Processing Services (Child of ECommerce)
                    pSpan.Value = orderProcId; pParent.Value = ecomId;
                    pService.Value = TelemetryConstants.ServiceNames.OrderProcessingServices; pName.Value = "OrderProcessor.HandleQueue";
                    pDuration.Value = isErr ? 185.0 : 85.0; pStatus.Value = isErr ? errorStatusCode : okStatusCode;
                    insertTraceCmd.ExecuteNonQuery();

                    // 6. Customer Survey Services (Child of Order Processing)
                    pSpan.Value = surveyId; pParent.Value = orderProcId;
                    pService.Value = TelemetryConstants.ServiceNames.CustomerSurveyServices; pName.Value = "SurveyService.ScheduleSurvey";
                    pDuration.Value = 120.0; pStatus.Value = okStatusCode;
                    insertTraceCmd.ExecuteNonQuery();
                }

                trans.Commit();
                Console.WriteLine("[KestrelScope] Seeded baseline trace spans across multi-service topology.");
            }
        }

        // Seed baseline logs if Logs table has no recent entries in the last 30 minutes
        using (var logsCountCmd = connection.CreateCommand())
        {
            logsCountCmd.CommandText = "SELECT COUNT(*) FROM Logs WHERE Timestamp >= @since;";
            logsCountCmd.Parameters.AddWithValue("@since", DateTime.UtcNow.AddMinutes(-TelemetryConstants.SeedDefaults.LogsRecentCheckMinutes).ToString("o"));
            long count = (long)(logsCountCmd.ExecuteScalar() ?? 0L);
            if (count == 0)
            {
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

                pService.Value = TelemetryConstants.ServiceNames.DefaultOrderService;
                pAttrs.Value = "{}";

                foreach (var log in baselineLogs)
                {
                    pTime.Value = now.AddMinutes(-log.offsetMin).ToString("o");
                    pSevText.Value = log.sevText;
                    pSevNum.Value = log.sevNum;
                    pBody.Value = log.body;
                    pTraceId.Value = log.trace ?? (object)DBNull.Value;
                    pSpanId.Value = log.span ?? (object)DBNull.Value;
                    insertLogCmd.ExecuteNonQuery();
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
                    pService.Value = log.svc;
                    pTime.Value = now.AddMinutes(-log.offsetMin).ToString("o");
                    pSevText.Value = log.sevText;
                    pSevNum.Value = log.sevNum;
                    pBody.Value = log.body;
                    pTraceId.Value = DBNull.Value;
                    pSpanId.Value = DBNull.Value;
                    insertLogCmd.ExecuteNonQuery();
                }

                Console.WriteLine("[KestrelScope] Seeded baseline log records.");
            }
        }

        // Seed default DatabaseSettings if not present
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

        foreach (var kvp in defaultSettings)
        {
            using var settingCmd = connection.CreateCommand();
            settingCmd.CommandText = @"
                INSERT OR IGNORE INTO DatabaseSettings (Key, Value, UpdatedAt, UpdatedBy)
                VALUES (@key, @value, CURRENT_TIMESTAMP, 'System');";
            settingCmd.Parameters.AddWithValue("@key", kvp.Key);
            settingCmd.Parameters.AddWithValue("@value", kvp.Value);
            settingCmd.ExecuteNonQuery();
        }

        Console.WriteLine("[KestrelScope] SQLite database initialized successfully in WAL mode.");
    }
}


using Microsoft.Data.Sqlite;
using System;

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
            pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
            pragmaCmd.ExecuteNonQuery();
        }

        const string schemaSql = @"
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT UNIQUE NOT NULL,
                PasswordHash TEXT NOT NULL,
                Role TEXT NOT NULL DEFAULT 'standard',
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
            alterCmd.CommandText = "ALTER TABLE Users ADD COLUMN Role TEXT NOT NULL DEFAULT 'standard';";
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
                    VALUES (@username, @passwordHash, 'admin');";
                insertCmd.Parameters.AddWithValue("@username", "admin");
                insertCmd.Parameters.AddWithValue("@passwordHash", Services.PasswordHasher.HashPassword("admin"));
                insertCmd.ExecuteNonQuery();
                Console.WriteLine("[KestrelScope] Seeded default administrator user: 'admin'");
            }
            else
            {
                // Ensure default admin user has admin role if previously seeded
                using var ensureAdminRoleCmd = connection.CreateCommand();
                ensureAdminRoleCmd.CommandText = "UPDATE Users SET Role = 'admin' WHERE Username = 'admin' AND (Role IS NULL OR Role = '' OR Role = 'standard');";
                ensureAdminRoleCmd.ExecuteNonQuery();
            }
        }

        // Seed initial telemetry if MetricSamples table is empty
        // Seed baseline telemetry if no samples exist within the last 24 hours
        using (var metricsCountCmd = connection.CreateCommand())
        {
            metricsCountCmd.CommandText = "SELECT COUNT(*) FROM MetricSamples WHERE Timestamp >= @since;";
            metricsCountCmd.Parameters.AddWithValue("@since", DateTime.UtcNow.AddHours(-24).ToString("o"));
            long count = (long)(metricsCountCmd.ExecuteScalar() ?? 0L);
            if (count == 0)
            {
                var rand = new Random(42);
                var now = DateTime.UtcNow;
                int[] minuteOffsets = [1400, 1200, 1000, 800, 600, 450, 300, 200, 120, 80, 50, 40, 30, 20, 14, 12, 10, 8, 6, 4, 3, 2, 1, 0];

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

                pService.Value = "order-service";

                foreach (int off in minuteOffsets)
                {
                    var sampleTime = now.AddMinutes(-off).ToString("o");
                    double latencyVal = 65 + 30 * Math.Sin(off / 30.0) + rand.NextDouble() * 25;
                    double ordersVal = 1 + rand.Next(0, 3);

                    pMetric.Value = "http.server.request.duration";
                    pVal.Value = Math.Round(latencyVal, 2);
                    pTime.Value = sampleTime;
                    insertSampleCmd.ExecuteNonQuery();

                    pMetric.Value = "orders.created.count";
                    pVal.Value = ordersVal;
                    pTime.Value = sampleTime;
                    insertSampleCmd.ExecuteNonQuery();
                }

                trans.Commit();
                Console.WriteLine("[KestrelScope] Seeded baseline 24-hour telemetry samples for 'order-service'.");
            }
        }

        // Seed baseline traces if Traces table has no recent spans
        using (var tracesCountCmd = connection.CreateCommand())
        {
            tracesCountCmd.CommandText = "SELECT COUNT(*) FROM Traces WHERE Timestamp >= @since;";
            tracesCountCmd.Parameters.AddWithValue("@since", DateTime.UtcNow.AddHours(-24).ToString("o"));
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

                int[] traceOffsets = [25, 12, 2];
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
                    pService.Value = "order-service"; pName.Value = "POST /api/orders";
                    pDuration.Value = 78.5; pStatus.Value = "1"; pTime.Value = time;
                    insertTraceCmd.ExecuteNonQuery();

                    // Inventory step
                    pSpan.Value = invId; pParent.Value = rootId; pName.Value = "InventoryService.ValidateStock";
                    pDuration.Value = 16.0; pStatus.Value = "1";
                    insertTraceCmd.ExecuteNonQuery();

                    // Payment step
                    pSpan.Value = payId; pParent.Value = rootId; pName.Value = "PaymentGateway.ChargeCard";
                    pDuration.Value = 39.5; pStatus.Value = "1";
                    insertTraceCmd.ExecuteNonQuery();

                    // DB step
                    pSpan.Value = dbId; pParent.Value = rootId; pName.Value = "OrderRepository.SaveOrder";
                    pDuration.Value = 20.0; pStatus.Value = "1";
                    insertTraceCmd.ExecuteNonQuery();
                }

                trans.Commit();
                Console.WriteLine("[KestrelScope] Seeded baseline trace spans.");
            }
        }

        // Seed baseline logs if Logs table has no recent entries
        using (var logsCountCmd = connection.CreateCommand())
        {
            logsCountCmd.CommandText = "SELECT COUNT(*) FROM Logs;";
            logsCountCmd.CommandText = "SELECT COUNT(*) FROM Logs WHERE Timestamp >= @since;";
            logsCountCmd.Parameters.AddWithValue("@since", DateTime.UtcNow.AddHours(-24).ToString("o"));
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
                    (45, "INFO", 9, "Application host started successfully in production environment.", null, null),
                    (30, "INFO", 9, "Connection pool initialized to internal storage engine.", null, null),
                    (15, "WARN", 13, "Degraded query performance detected on cold cache access.", null, null),
                    (5, "INFO", 9, "Batch telemetry sync completed for order-processor-service.", null, null),
                    (5, "INFO", 9, "Batch telemetry sync completed for order-service.", null, null),
                    (1, "INFO", 9, "System health check status reported OK.", null, null)
                };

                pService.Value = "order-processor-service";
                pService.Value = "order-service";
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

                Console.WriteLine("[KestrelScope] Seeded baseline log records.");
            }
        }

        Console.WriteLine("[KestrelScope] SQLite database initialized successfully in WAL mode.");
    }
}


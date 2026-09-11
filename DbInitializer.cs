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
        ";

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = schemaSql;
            cmd.ExecuteNonQuery();
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
                    INSERT INTO Users (Username, PasswordHash) 
                    VALUES (@username, @passwordHash);";
                insertCmd.Parameters.AddWithValue("@username", "admin");
                insertCmd.Parameters.AddWithValue("@passwordHash", Services.PasswordHasher.HashPassword("admin"));
                insertCmd.ExecuteNonQuery();
                Console.WriteLine("[KestrelScope] Seeded default administrator user: 'admin'");
            }
        }

        // Seed initial telemetry if MetricSamples table is empty
        using (var metricsCountCmd = connection.CreateCommand())
        {
            metricsCountCmd.CommandText = "SELECT COUNT(*) FROM MetricSamples;";
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

                pService.Value = "order-processor-service";

                foreach (int off in minuteOffsets)
                {
                    var sampleTime = now.AddMinutes(-off).ToString("o");
                    double latency = 160 + 100 * Math.Sin(off / 60.0) + rand.NextDouble() * 80;
                    double memory = 260 + rand.NextDouble() * 50;

                    pMetric.Value = "http.server.duration";
                    pVal.Value = Math.Round(latency, 2);
                    pTime.Value = sampleTime;
                    insertSampleCmd.ExecuteNonQuery();

                    pMetric.Value = "process.memory.usage";
                    pVal.Value = Math.Round(memory, 2);
                    pTime.Value = sampleTime;
                    insertSampleCmd.ExecuteNonQuery();
                }

                trans.Commit();
                Console.WriteLine("[KestrelScope] Seeded baseline 24-hour telemetry samples.");
            }
        }

        Console.WriteLine("[KestrelScope] SQLite database initialized successfully in WAL mode.");
    }
}


using Microsoft.Data.Sqlite;
using System;

namespace KestrelScope;

public static class DbInitializer
{
    public static void Initialize(string connectionString)
    {
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

        Console.WriteLine("[KestrelScope] SQLite database initialized successfully in WAL mode.");
    }
}


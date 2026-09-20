using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using KestrelScope.Constants;
using KestrelScope.Models;

namespace KestrelScope.Services;

public class DatabaseManagementService : IDatabaseManagementService
{
    private readonly string _connectionString;
    private string _dbFilePath;
    private readonly string _backupDir;
    private readonly ILogger<DatabaseManagementService> _logger;

    public DatabaseManagementService(IConfiguration configuration, ILogger<DatabaseManagementService> logger)
    {
        _logger = logger;
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? AppConstants.Database.DefaultConnectionString;

        var csBuilder = new SqliteConnectionStringBuilder(_connectionString);
        string dataSource = csBuilder.DataSource;
        if (Path.IsPathRooted(dataSource))
        {
            _dbFilePath = dataSource;
        }
        else
        {
            string cwdPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), dataSource));
            string baseDirPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, dataSource));
            _dbFilePath = File.Exists(cwdPath) ? cwdPath : (File.Exists(baseDirPath) ? baseDirPath : cwdPath);
        }

        string configuredBackupDir = configuration["DatabaseManagement:BackupDirectory"] 
            ?? AppConstants.DatabaseManagement.DefaultBackupDirectory;

        if (Path.IsPathRooted(configuredBackupDir))
        {
            _backupDir = configuredBackupDir;
        }
        else
        {
            string baseDir = Path.GetDirectoryName(_dbFilePath) ?? Directory.GetCurrentDirectory();
            _backupDir = Path.GetFullPath(Path.Combine(baseDir, configuredBackupDir));
        }

        if (!Directory.Exists(_backupDir))
        {
            Directory.CreateDirectory(_backupDir);
        }
    }

    #region Storage Statistics & Health
    public async Task<DatabaseStorageStatsDto> GetStorageStatsAsync()
    {
        var stats = new DatabaseStorageStatsDto();

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string activeDbPath = _dbFilePath;
        try
        {
            var dbList = await conn.QueryAsync<(int seq, string name, string file)>("PRAGMA database_list;");
            var mainDb = dbList.FirstOrDefault(d => d.name == "main");
            if (!string.IsNullOrEmpty(mainDb.file) && File.Exists(mainDb.file))
            {
                activeDbPath = mainDb.file;
                _dbFilePath = mainDb.file;
            }
        }
        catch { }

        // Check file sizes
        if (File.Exists(activeDbPath))
        {
            var fi = new FileInfo(activeDbPath);
            stats.DatabaseSizeBytes = fi.Length;
            stats.DatabaseSizeFormatted = FormatBytes(fi.Length);
        }

        string walPath = activeDbPath + "-wal";
        if (File.Exists(walPath))
        {
            var fiWal = new FileInfo(walPath);
            stats.WalSizeBytes = fiWal.Length;
            stats.WalSizeFormatted = FormatBytes(fiWal.Length);
        }

        string shmPath = activeDbPath + "-shm";
        if (File.Exists(shmPath))
        {
            var fiShm = new FileInfo(shmPath);
            stats.ShmSizeBytes = fiShm.Length;
            stats.ShmSizeFormatted = FormatBytes(fiShm.Length);
        }

        // Backups folder size
        if (Directory.Exists(_backupDir))
        {
            var backupFiles = Directory.GetFiles(_backupDir, "*.*")
                .Where(f => f.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                .ToList();
            stats.BackupCount = backupFiles.Count;
            long totalBackupBytes = backupFiles.Sum(f => new FileInfo(f).Length);
            stats.TotalBackupsSizeBytes = totalBackupBytes;
            stats.TotalBackupsSizeFormatted = FormatBytes(totalBackupBytes);
        }

        stats.PageCount = await conn.ExecuteScalarAsync<long>("PRAGMA page_count;");
        stats.PageSizeBytes = await conn.ExecuteScalarAsync<long>("PRAGMA page_size;");
        stats.FreelistCount = await conn.ExecuteScalarAsync<long>("PRAGMA freelist_count;");
        stats.FreelistSizeBytes = stats.FreelistCount * stats.PageSizeBytes;
        stats.FreelistSizeFormatted = FormatBytes(stats.FreelistSizeBytes);

        // Table Breakdown
        var tables = new (string Name, string? TimeCol)[]
        {
            ("MetricSamples", "Timestamp"),
            ("Traces", "Timestamp"),
            ("Logs", "Timestamp"),
            ("AlertRules", null),
            ("Users", "CreatedAt"),
            ("DatabaseSettings", "UpdatedAt"),
            ("AdminAuditLogs", "Timestamp")
        };

        foreach (var (tName, tCol) in tables)
        {
            var tableStat = new TableStorageStatDto { TableName = tName };
            try
            {
                tableStat.RowCount = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {tName};");
                if (tCol != null && tableStat.RowCount > 0)
                {
                    var times = await conn.QuerySingleOrDefaultAsync<(string? Oldest, string? Newest)>(
                        $"SELECT MIN({tCol}) as Oldest, MAX({tCol}) as Newest FROM {tName};"
                    );
                    if (!string.IsNullOrEmpty(times.Oldest) && DateTime.TryParse(times.Oldest, out var od))
                        tableStat.OldestRecord = od;
                    if (!string.IsNullOrEmpty(times.Newest) && DateTime.TryParse(times.Newest, out var nd))
                        tableStat.NewestRecord = nd;
                }

                int approxRowBytes = GetApproxRowBytes(tName);
                tableStat.EstimatedSizeBytes = tableStat.RowCount * approxRowBytes;
                tableStat.EstimatedSizeFormatted = FormatBytes(tableStat.EstimatedSizeBytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query storage stats for table {Table}", tName);
            }
            stats.Tables.Add(tableStat);
        }

        return stats;
    }

    public async Task<DatabaseHealthDto> CheckHealthAsync()
    {
        var health = new DatabaseHealthDto();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // PRAGMA integrity_check
        var integrityLines = (await conn.QueryAsync<string>("PRAGMA integrity_check(100);")).ToList();
        health.IntegrityCheckOutput = string.Join("\n", integrityLines);

        // PRAGMA foreign_key_check
        var fkLines = (await conn.QueryAsync<string>("PRAGMA foreign_key_check;")).ToList();
        health.ForeignKeyCheckOutput = fkLines.Count > 0 ? string.Join("\n", fkLines) : "ok";

        bool integrityOk = integrityLines.Count == 1 && string.Equals(integrityLines[0], "ok", StringComparison.OrdinalIgnoreCase);
        bool fkOk = health.ForeignKeyCheckOutput == "ok";

        health.IsHealthy = integrityOk && fkOk;
        health.Status = health.IsHealthy ? "Healthy" : (integrityOk ? "Degraded" : "Corrupted");

        return health;
    }
    #endregion

    #region Retention Policies
    public async Task<RetentionPolicyDto> GetRetentionPoliciesAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var settings = (await conn.QueryAsync<(string Key, string Value, string? UpdatedAt, string? UpdatedBy)>(
            "SELECT Key, Value, UpdatedAt, UpdatedBy FROM DatabaseSettings;"
        )).ToDictionary(x => x.Key, x => x);

        var dto = new RetentionPolicyDto
        {
            MetricsRetentionDays = GetIntSetting(settings, "RetentionMetricsDays", AppConstants.DatabaseManagement.DefaultRetentionMetricsDays),
            TracesRetentionDays = GetIntSetting(settings, "RetentionTracesDays", AppConstants.DatabaseManagement.DefaultRetentionTracesDays),
            LogsRetentionDays = GetIntSetting(settings, "RetentionLogsDays", AppConstants.DatabaseManagement.DefaultRetentionLogsDays),
            AlertsRetentionDays = GetIntSetting(settings, "RetentionAlertsDays", AppConstants.DatabaseManagement.DefaultRetentionAlertsDays),
            AuditLogsRetentionDays = GetIntSetting(settings, "RetentionAuditLogsDays", AppConstants.DatabaseManagement.DefaultRetentionAuditLogsDays),
            AutoPruneEnabled = GetBoolSetting(settings, "AutoPruneEnabled", AppConstants.DatabaseManagement.DefaultAutoPruneEnabled),
            AutoPruneHourUtc = GetIntSetting(settings, "AutoPruneHourUtc", AppConstants.DatabaseManagement.DefaultAutoPruneHourUtc),
            AutoBackupEnabled = GetBoolSetting(settings, "AutoBackupEnabled", AppConstants.DatabaseManagement.DefaultAutoBackupEnabled),
            AutoBackupHourUtc = GetIntSetting(settings, "AutoBackupHourUtc", AppConstants.DatabaseManagement.DefaultAutoBackupHourUtc),
            BackupRetentionCount = GetIntSetting(settings, "BackupRetentionCount", AppConstants.DatabaseManagement.DefaultBackupRetentionCount),
            StorageWarningThresholdMb = GetLongSetting(settings, "StorageWarningThresholdMb", AppConstants.DatabaseManagement.DefaultStorageWarningThresholdMb)
        };

        if (settings.Values.Any(s => !string.IsNullOrEmpty(s.UpdatedAt)))
        {
            var maxUpdated = settings.Values.Where(s => !string.IsNullOrEmpty(s.UpdatedAt))
                .Select(s => DateTime.TryParse(s.UpdatedAt, out var dt) ? dt : (DateTime?)null)
                .Where(dt => dt.HasValue)
                .Max();
            dto.UpdatedAt = maxUpdated;
            dto.UpdatedBy = settings.Values.FirstOrDefault(s => !string.IsNullOrEmpty(s.UpdatedBy)).UpdatedBy;
        }

        return dto;
    }

    public async Task<RetentionPolicyDto> UpdateRetentionPoliciesAsync(UpdateRetentionPolicyRequest req, string username, string ipAddress)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var updates = new Dictionary<string, string>
        {
            ["RetentionMetricsDays"] = Math.Max(1, req.MetricsRetentionDays).ToString(),
            ["RetentionTracesDays"] = Math.Max(1, req.TracesRetentionDays).ToString(),
            ["RetentionLogsDays"] = Math.Max(1, req.LogsRetentionDays).ToString(),
            ["RetentionAlertsDays"] = Math.Max(1, req.AlertsRetentionDays).ToString(),
            ["RetentionAuditLogsDays"] = Math.Max(1, req.AuditLogsRetentionDays).ToString(),
            ["AutoPruneEnabled"] = req.AutoPruneEnabled.ToString().ToLowerInvariant(),
            ["AutoPruneHourUtc"] = Math.Clamp(req.AutoPruneHourUtc, 0, 23).ToString(),
            ["AutoBackupEnabled"] = req.AutoBackupEnabled.ToString().ToLowerInvariant(),
            ["AutoBackupHourUtc"] = Math.Clamp(req.AutoBackupHourUtc, 0, 23).ToString(),
            ["BackupRetentionCount"] = Math.Max(1, req.BackupRetentionCount).ToString(),
            ["StorageWarningThresholdMb"] = Math.Max(100, req.StorageWarningThresholdMb).ToString()
        };

        using var trans = conn.BeginTransaction();
        foreach (var (k, v) in updates)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO DatabaseSettings (Key, Value, UpdatedAt, UpdatedBy)
                VALUES (@Key, @Value, CURRENT_TIMESTAMP, @username)
                ON CONFLICT(Key) DO UPDATE SET
                    Value = excluded.Value,
                    UpdatedAt = CURRENT_TIMESTAMP,
                    UpdatedBy = excluded.UpdatedBy;",
                new { Key = k, Value = v, username },
                trans
            );
        }
        trans.Commit();

        await LogAuditAsync(username, null, AppConstants.AuditActions.RetentionUpdate, "DatabaseSettings", updates, ipAddress);

        return await GetRetentionPoliciesAsync();
    }
    #endregion

    #region Pruning & Space Reclamation
    public async Task<PruneResultDto> ExecutePruneAsync(PruneRequest req, string username, string ipAddress)
    {
        var sw = Stopwatch.StartNew();
        var policies = await GetRetentionPoliciesAsync();

        var result = new PruneResultDto
        {
            DryRun = req.DryRun,
            Target = req.Target
        };

        string target = req.Target.ToLowerInvariant();
        bool pruneMetrics = target == "all" || target == "metrics";
        bool pruneTraces = target == "all" || target == "traces";
        bool pruneLogs = target == "all" || target == "logs";
        bool pruneAudit = target == "all" || target == "audit_logs";

        int metricsDays = req.CustomRetentionDays ?? policies.MetricsRetentionDays;
        int tracesDays = req.CustomRetentionDays ?? policies.TracesRetentionDays;
        int logsDays = req.CustomRetentionDays ?? policies.LogsRetentionDays;
        int auditDays = req.CustomRetentionDays ?? policies.AuditLogsRetentionDays;

        string metricsCutoff = DateTime.UtcNow.AddDays(-metricsDays).ToString("o");
        string tracesCutoff = DateTime.UtcNow.AddDays(-tracesDays).ToString("o");
        string logsCutoff = DateTime.UtcNow.AddDays(-logsDays).ToString("o");
        string auditCutoff = DateTime.UtcNow.AddDays(-auditDays).ToString("o");

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        if (req.DryRun)
        {
            if (pruneMetrics) result.DeletedCounts["MetricSamples"] = await CountOlderThanAsync(conn, "MetricSamples", metricsCutoff);
            if (pruneTraces) result.DeletedCounts["Traces"] = await CountOlderThanAsync(conn, "Traces", tracesCutoff);
            if (pruneLogs) result.DeletedCounts["Logs"] = await CountOlderThanAsync(conn, "Logs", logsCutoff);
            if (pruneAudit) result.DeletedCounts["AdminAuditLogs"] = await CountOlderThanAsync(conn, "AdminAuditLogs", auditCutoff);
        }
        else
        {
            int batchSize = AppConstants.DatabaseManagement.DefaultPruneBatchSize;

            if (pruneMetrics) result.DeletedCounts["MetricSamples"] = await BatchDeleteOlderThanAsync(conn, "MetricSamples", "Id", metricsCutoff, batchSize);
            if (pruneTraces) result.DeletedCounts["Traces"] = await BatchDeleteOlderThanAsync(conn, "Traces", "SpanId", tracesCutoff, batchSize);
            if (pruneLogs) result.DeletedCounts["Logs"] = await BatchDeleteOlderThanAsync(conn, "Logs", "Id", logsCutoff, batchSize);
            if (pruneAudit) result.DeletedCounts["AdminAuditLogs"] = await BatchDeleteOlderThanAsync(conn, "AdminAuditLogs", "Id", auditCutoff, batchSize);

            if (req.RunCheckpoint)
            {
                await conn.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE);");
                result.CheckpointExecuted = true;
            }

            if (req.RunVacuum)
            {
                await conn.ExecuteAsync("VACUUM;");
                result.VacuumExecuted = true;
            }

            await LogAuditAsync(username, null, AppConstants.AuditActions.Prune, target, new
            {
                deletedCounts = result.DeletedCounts,
                vacuum = result.VacuumExecuted,
                checkpoint = result.CheckpointExecuted
            }, ipAddress);
        }

        sw.Stop();
        result.ElapsedMs = sw.ElapsedMilliseconds;
        result.TotalRowsDeleted = result.DeletedCounts.Values.Sum();

        long estimatedBytesFreed = result.DeletedCounts.Sum(kv => kv.Value * GetApproxRowBytes(kv.Key));

        result.FreedBytesEstimated = estimatedBytesFreed;
        result.FreedSizeFormatted = FormatBytes(estimatedBytesFreed);

        return result;
    }

    private static async Task<long> CountOlderThanAsync(SqliteConnection conn, string tableName, string cutoff)
    {
        return await conn.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM {tableName} WHERE Timestamp < @cutoff;",
            new { cutoff }
        );
    }

    private static async Task<long> BatchDeleteOlderThanAsync(SqliteConnection conn, string tableName, string idCol, string cutoff, int batchSize)
    {
        long total = 0;
        while (true)
        {
            int affected = await conn.ExecuteAsync($@"
                DELETE FROM {tableName} 
                WHERE {idCol} IN (
                    SELECT {idCol} FROM {tableName} 
                    WHERE Timestamp < @cutoff 
                    LIMIT @batchSize
                );",
                new { cutoff, batchSize }
            );
            total += affected;
            if (affected < batchSize) break;
        }
        return total;
    }

    public async Task<string> RunVacuumAsync(string username, string ipAddress)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("VACUUM;");
        await LogAuditAsync(username, null, AppConstants.AuditActions.Vacuum, "Database", new { action = "VACUUM completed" }, ipAddress);
        return "VACUUM completed successfully.";
    }

    public async Task<string> RunWalCheckpointAsync(string username, string ipAddress)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE);");
        await LogAuditAsync(username, null, AppConstants.AuditActions.Checkpoint, "WAL", new { action = "WAL checkpoint TRUNCATE completed" }, ipAddress);
        return "WAL checkpoint (TRUNCATE) completed successfully.";
    }
    #endregion

    #region Online Backups
    public async Task<BackupItemDto> CreateBackupAsync(CreateBackupRequest req, string username, string ipAddress, string backupType = "Manual")
    {
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string safeLabel = string.IsNullOrWhiteSpace(req.Label) 
            ? string.Empty 
            : "-" + string.Join("-", req.Label.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

        string tempSnapshotPath = Path.Combine(_backupDir, $"snapshot-temp-{Guid.NewGuid():N}.db");

        try
        {
            // Execute non-blocking online backup via SQLite Backup API
            using (var sourceConn = new SqliteConnection(_connectionString))
            {
                await sourceConn.OpenAsync();
                using var destConn = new SqliteConnection($"Data Source={tempSnapshotPath};");
                await destConn.OpenAsync();
                sourceConn.BackupDatabase(destConn);
            }

            string finalFileName;
            string finalFilePath;

            if (req.Compress)
            {
                finalFileName = $"kestrelscope-backup-{timestamp}{safeLabel}.db.gz";
                finalFilePath = Path.Combine(_backupDir, finalFileName);

                using (var src = File.OpenRead(tempSnapshotPath))
                using (var dest = File.Create(finalFilePath))
                using (var gz = new GZipStream(dest, CompressionLevel.Optimal))
                {
                    await src.CopyToAsync(gz);
                }

                File.Delete(tempSnapshotPath);
            }
            else
            {
                finalFileName = $"kestrelscope-backup-{timestamp}{safeLabel}.db";
                finalFilePath = Path.Combine(_backupDir, finalFileName);
                File.Move(tempSnapshotPath, finalFilePath);
            }

            var fi = new FileInfo(finalFilePath);
            string sha256 = await ComputeSha256Async(finalFilePath);

            var item = new BackupItemDto
            {
                FileName = finalFileName,
                CreatedAt = fi.CreationTimeUtc,
                SizeBytes = fi.Length,
                SizeFormatted = FormatBytes(fi.Length),
                IsCompressed = req.Compress,
                ChecksumSha256 = sha256,
                Type = backupType,
                Label = req.Label
            };

            await LogAuditAsync(username, null, AppConstants.AuditActions.BackupCreate, finalFileName, new
            {
                sizeBytes = fi.Length,
                compressed = req.Compress,
                sha256,
                type = backupType,
                label = req.Label
            }, ipAddress);

            return item;
        }
        finally
        {
            if (File.Exists(tempSnapshotPath))
            {
                try { File.Delete(tempSnapshotPath); } catch { }
            }
        }
    }

    public async Task<List<BackupItemDto>> GetBackupsAsync()
    {
        var list = new List<BackupItemDto>();
        if (!Directory.Exists(_backupDir)) return list;

        var files = Directory.GetFiles(_backupDir, "*.*")
            .Where(f => f.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .ToList();

        foreach (var fi in files)
        {
            bool isGz = fi.Name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
            string type = "Manual";
            if (fi.Name.Contains("safety", StringComparison.OrdinalIgnoreCase))
                type = "SafetySnapshot";
            else if (fi.Name.Contains("scheduled", StringComparison.OrdinalIgnoreCase))
                type = "Scheduled";

            list.Add(new BackupItemDto
            {
                FileName = fi.Name,
                CreatedAt = fi.CreationTimeUtc,
                SizeBytes = fi.Length,
                SizeFormatted = FormatBytes(fi.Length),
                IsCompressed = isGz,
                ChecksumSha256 = await ComputeSha256Async(fi.FullName),
                Type = type
            });
        }

        return list;
    }

    public Task<(Stream FileStream, string ContentType, string FileName)> GetBackupDownloadStreamAsync(string fileName, string username, string ipAddress)
    {
        string safeName = Path.GetFileName(fileName);
        string fullPath = Path.Combine(_backupDir, safeName);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Backup file '{safeName}' does not exist.");
        }

        string contentType = safeName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? "application/gzip"
            : "application/x-sqlite3";

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult((stream, contentType, safeName));
    }

    public async Task<bool> DeleteBackupAsync(string fileName, string username, string ipAddress)
    {
        string safeName = Path.GetFileName(fileName);
        string fullPath = Path.Combine(_backupDir, safeName);
        if (!File.Exists(fullPath)) return false;

        File.Delete(fullPath);

        await LogAuditAsync(username, null, AppConstants.AuditActions.BackupDelete, safeName, new { deleted = true }, ipAddress);
        return true;
    }
    #endregion

    #region Disaster Recovery & Restore
    public async Task<RestoreResultDto> RestoreBackupAsync(RestoreRequest req, string username, string ipAddress)
    {
        if (!string.Equals(req.ConfirmationToken?.Trim(), AppConstants.DatabaseManagement.RestoreConfirmationKeyword, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid confirmation token. You must provide '{AppConstants.DatabaseManagement.RestoreConfirmationKeyword}' to authorize a restore operation.");
        }

        string safeBackupName = Path.GetFileName(req.BackupFileName);
        string backupFilePath = Path.Combine(_backupDir, safeBackupName);

        if (!File.Exists(backupFilePath))
        {
            throw new FileNotFoundException($"Target backup file '{safeBackupName}' not found in backup repository.");
        }

        var sw = Stopwatch.StartNew();
        string tempExtractedDb = Path.Combine(_backupDir, $"restore-extract-{Guid.NewGuid():N}.db");

        try
        {
            // 1. If compressed, decompress to temp file
            if (safeBackupName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                using var src = File.OpenRead(backupFilePath);
                using var gz = new GZipStream(src, CompressionMode.Decompress);
                using var dest = File.Create(tempExtractedDb);
                await gz.CopyToAsync(dest);
            }
            else
            {
                File.Copy(backupFilePath, tempExtractedDb, overwrite: true);
            }

            // 2. Pre-flight check on target backup file
            using (var testConn = new SqliteConnection($"Data Source={tempExtractedDb};"))
            {
                await testConn.OpenAsync();
                var quickCheck = await testConn.ExecuteScalarAsync<string>("PRAGMA quick_check;");
                if (!string.Equals(quickCheck, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Pre-flight check failed. Backup file appears corrupt: {quickCheck}");
                }
            }

            // 3. Create automated pre-restore safety snapshot of active database
            var safetySnapshot = await CreateBackupAsync(new CreateBackupRequest
            {
                Label = "pre-restore-safety",
                Compress = false
            }, username, ipAddress, "SafetySnapshot");

            // 4. Restore: Use SQLite Online Backup API to copy from restored db into main active database
            using (var sourceConn = new SqliteConnection($"Data Source={tempExtractedDb};"))
            {
                await sourceConn.OpenAsync();
                using var destConn = new SqliteConnection(_connectionString);
                await destConn.OpenAsync();
                sourceConn.BackupDatabase(destConn);
            }

            // 5. Initialize/migrate schema if restored backup was from an older version
            DbInitializer.Initialize(_connectionString);

            // 6. Post-restore integrity check
            var health = await CheckHealthAsync();

            sw.Stop();

            await LogAuditAsync(username, null, AppConstants.AuditActions.BackupRestore, safeBackupName, new
            {
                restoredFrom = safeBackupName,
                safetySnapshot = safetySnapshot.FileName,
                elapsedMs = sw.ElapsedMilliseconds,
                healthStatus = health.Status
            }, ipAddress);

            return new RestoreResultDto
            {
                Status = health.IsHealthy ? "success" : "warning",
                SafetySnapshotFileName = safetySnapshot.FileName,
                RestoredFrom = safeBackupName,
                ElapsedMs = sw.ElapsedMilliseconds,
                IntegrityCheckOutput = health.IntegrityCheckOutput
            };
        }
        finally
        {
            if (File.Exists(tempExtractedDb))
            {
                try { File.Delete(tempExtractedDb); } catch { }
            }
        }
    }
    #endregion

    #region Audit Logging
    public async Task<List<AdminAuditLogDto>> GetAuditLogsAsync(int limit = 100, int offset = 0)
    {
        if (limit <= 0 || limit > 500) limit = 100;
        if (offset < 0) offset = 0;

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var logs = await conn.QueryAsync<AdminAuditLogDto>(@"
            SELECT Id, Timestamp, UserId, Username, Action, Target, DetailsJson, IpAddress, Status
            FROM AdminAuditLogs
            ORDER BY Timestamp DESC, Id DESC
            LIMIT @limit OFFSET @offset;",
            new { limit, offset }
        );

        return logs.ToList();
    }

    public async Task LogAuditAsync(string username, string? userId, string action, string? target, object? details, string? ipAddress, string status = "SUCCESS")
    {
        try
        {
            string? detailsJson = details != null ? JsonSerializer.Serialize(details) : null;

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(@"
                INSERT INTO AdminAuditLogs (Timestamp, UserId, Username, Action, Target, DetailsJson, IpAddress, Status)
                VALUES (CURRENT_TIMESTAMP, @userId, @username, @action, @target, @detailsJson, @ipAddress, @status);",
                new { userId, username, action, target, detailsJson, ipAddress, status }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write administrative audit log entry.");
        }
    }
    #endregion

    #region Helper Utilities
    private static int GetApproxRowBytes(string tableName) => tableName switch
    {
        "MetricSamples" => 45,
        "Traces" => 150,
        "Logs" => 280,
        "AlertRules" => 120,
        "Users" => 180,
        "DatabaseSettings" => 100,
        "AdminAuditLogs" => 160,
        _ => 100
    };

    private static int GetIntSetting(Dictionary<string, (string Key, string Value, string? UpdatedAt, string? UpdatedBy)> settings, string key, int fallback) =>
        settings.TryGetValue(key, out var row) && int.TryParse(row.Value, out int val) ? val : fallback;

    private static long GetLongSetting(Dictionary<string, (string Key, string Value, string? UpdatedAt, string? UpdatedBy)> settings, string key, long fallback) =>
        settings.TryGetValue(key, out var row) && long.TryParse(row.Value, out long val) ? val : fallback;

    private static bool GetBoolSetting(Dictionary<string, (string Key, string Value, string? UpdatedAt, string? UpdatedBy)> settings, string key, bool fallback) =>
        settings.TryGetValue(key, out var row) && bool.TryParse(row.Value, out bool val) ? val : fallback;

    private static async Task<string> ComputeSha256Async(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hash = await sha.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024m) >= 1)
        {
            number /= 1024m;
            counter++;
            if (counter == suffixes.Length - 1) break;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
    #endregion
}

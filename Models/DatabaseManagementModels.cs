using System;
using System.Collections.Generic;

namespace KestrelScope.Models;

public class DatabaseStorageStatsDto
{
    public long DatabaseSizeBytes { get; set; }
    public string DatabaseSizeFormatted { get; set; } = "0 B";
    public long WalSizeBytes { get; set; }
    public string WalSizeFormatted { get; set; } = "0 B";
    public long ShmSizeBytes { get; set; }
    public string ShmSizeFormatted { get; set; } = "0 B";
    public long TotalBackupsSizeBytes { get; set; }
    public string TotalBackupsSizeFormatted { get; set; } = "0 B";
    public int BackupCount { get; set; }
    public long PageCount { get; set; }
    public long PageSizeBytes { get; set; }
    public long FreelistCount { get; set; }
    public long FreelistSizeBytes { get; set; }
    public string FreelistSizeFormatted { get; set; } = "0 B";
    public long StorageWarningThresholdMb { get; set; }
    public bool IsStorageWarning { get; set; }
    public string? StorageWarningMessage { get; set; }
    public List<TableStorageStatDto> Tables { get; set; } = new();
}

public class TableStorageStatDto
{
    public string TableName { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public DateTime? OldestRecord { get; set; }
    public DateTime? NewestRecord { get; set; }
    public long EstimatedSizeBytes { get; set; }
    public string EstimatedSizeFormatted { get; set; } = "0 B";
}

public class DatabaseHealthDto
{
    public bool IsHealthy { get; set; }
    public string Status { get; set; } = "Healthy";
    public string IntegrityCheckOutput { get; set; } = "ok";
    public string ForeignKeyCheckOutput { get; set; } = "ok";
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}

public class RetentionPolicyDto
{
    public int MetricsRetentionDays { get; set; }
    public int TracesRetentionDays { get; set; }
    public int LogsRetentionDays { get; set; }
    public int AlertsRetentionDays { get; set; }
    public int AuditLogsRetentionDays { get; set; }
    public bool AutoPruneEnabled { get; set; }
    public int AutoPruneHourUtc { get; set; }
    public bool AutoBackupEnabled { get; set; }
    public int AutoBackupHourUtc { get; set; }
    public int BackupRetentionCount { get; set; }
    public long StorageWarningThresholdMb { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class UpdateRetentionPolicyRequest
{
    public int MetricsRetentionDays { get; set; }
    public int TracesRetentionDays { get; set; }
    public int LogsRetentionDays { get; set; }
    public int AlertsRetentionDays { get; set; }
    public int AuditLogsRetentionDays { get; set; }
    public bool AutoPruneEnabled { get; set; }
    public int AutoPruneHourUtc { get; set; }
    public bool AutoBackupEnabled { get; set; }
    public int AutoBackupHourUtc { get; set; }
    public int BackupRetentionCount { get; set; }
    public long StorageWarningThresholdMb { get; set; }
}

public class PruneRequest
{
    public string Target { get; set; } = "all"; // all, metrics, traces, logs, alerts, audit_logs
    public int? CustomRetentionDays { get; set; }
    public bool DryRun { get; set; } = false;
    public bool RunVacuum { get; set; } = false;
    public bool RunCheckpoint { get; set; } = true;
}

public class PruneResultDto
{
    public string Status { get; set; } = "success";
    public bool DryRun { get; set; }
    public string Target { get; set; } = "all";
    public Dictionary<string, long> DeletedCounts { get; set; } = new();
    public long TotalRowsDeleted { get; set; }
    public long ElapsedMs { get; set; }
    public long FreedBytesEstimated { get; set; }
    public string FreedSizeFormatted { get; set; } = "0 B";
    public bool VacuumExecuted { get; set; }
    public bool CheckpointExecuted { get; set; }
}

public class BackupItemDto
{
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long SizeBytes { get; set; }
    public string SizeFormatted { get; set; } = "0 B";
    public bool IsCompressed { get; set; }
    public string ChecksumSha256 { get; set; } = string.Empty;
    public string Type { get; set; } = "Manual"; // Manual, Scheduled, SafetySnapshot
    public string? Label { get; set; }
}

public class CreateBackupRequest
{
    public string? Label { get; set; }
    public bool Compress { get; set; } = true;
}

public class RestoreRequest
{
    public string BackupFileName { get; set; } = string.Empty;
    public string ConfirmationToken { get; set; } = string.Empty;
}

public class RestoreResultDto
{
    public string Status { get; set; } = "success";
    public string SafetySnapshotFileName { get; set; } = string.Empty;
    public string RestoredFrom { get; set; } = string.Empty;
    public long ElapsedMs { get; set; }
    public string IntegrityCheckOutput { get; set; } = "ok";
}

public class AdminAuditLogDto
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Target { get; set; }
    public string? DetailsJson { get; set; }
    public string? IpAddress { get; set; }
    public string Status { get; set; } = "SUCCESS";
}


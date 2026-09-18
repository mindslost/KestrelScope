using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using KestrelScope.Models;

namespace KestrelScope.Services;

public interface IDatabaseManagementService
{
    Task<DatabaseStorageStatsDto> GetStorageStatsAsync();
    Task<DatabaseHealthDto> CheckHealthAsync();
    Task<RetentionPolicyDto> GetRetentionPoliciesAsync();
    Task<RetentionPolicyDto> UpdateRetentionPoliciesAsync(UpdateRetentionPolicyRequest req, string username, string ipAddress);
    Task<PruneResultDto> ExecutePruneAsync(PruneRequest req, string username, string ipAddress);
    Task<BackupItemDto> CreateBackupAsync(CreateBackupRequest req, string username, string ipAddress, string backupType = "Manual");
    Task<List<BackupItemDto>> GetBackupsAsync();
    Task<(Stream FileStream, string ContentType, string FileName)> GetBackupDownloadStreamAsync(string fileName, string username, string ipAddress);
    Task<bool> DeleteBackupAsync(string fileName, string username, string ipAddress);
    Task<RestoreResultDto> RestoreBackupAsync(RestoreRequest req, string username, string ipAddress);
    Task<string> RunVacuumAsync(string username, string ipAddress);
    Task<string> RunWalCheckpointAsync(string username, string ipAddress);
    Task<List<AdminAuditLogDto>> GetAuditLogsAsync(int limit = 100, int offset = 0);
    Task LogAuditAsync(string username, string? userId, string action, string? target, object? details, string? ipAddress, string status = "SUCCESS");
}


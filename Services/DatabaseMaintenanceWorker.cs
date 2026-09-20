using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using KestrelScope.Constants;
using KestrelScope.Models;

namespace KestrelScope.Services;

public class DatabaseMaintenanceWorker : BackgroundService
{
    private const string SystemUser = "SystemScheduled";
    private const string LocalIp = "127.0.0.1";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseMaintenanceWorker> _logger;
    private DateTime? _lastPruneDateUtc;
    private DateTime? _lastBackupDateUtc;

    public DatabaseMaintenanceWorker(IServiceProvider serviceProvider, ILogger<DatabaseMaintenanceWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Database Maintenance Worker started.");

        // Initial brief pause before first evaluation
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseManagementService>();
                var policies = await dbService.GetRetentionPoliciesAsync();
                var nowUtc = DateTime.UtcNow;

                await CheckAndExecutePruneAsync(dbService, policies, nowUtc);
                await CheckAndExecuteBackupAsync(dbService, policies, nowUtc);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error occurred during background database maintenance execution.");
            }

            // Check every 15 minutes
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }

        _logger.LogInformation("Database Maintenance Worker stopping.");
    }

    private async Task CheckAndExecutePruneAsync(IDatabaseManagementService dbService, RetentionPolicyDto policies, DateTime nowUtc)
    {
        if (!policies.AutoPruneEnabled || nowUtc.Hour != policies.AutoPruneHourUtc || _lastPruneDateUtc?.Date == nowUtc.Date)
        {
            return;
        }

        _logger.LogInformation("Triggering scheduled retention pruning cycle for {Date}", nowUtc.ToString("yyyy-MM-dd"));
        var pruneResult = await dbService.ExecutePruneAsync(new PruneRequest
        {
            Target = "all",
            DryRun = false,
            RunCheckpoint = true,
            RunVacuum = false
        }, SystemUser, LocalIp);

        _logger.LogInformation("Scheduled pruning complete: {TotalRows} rows purged in {Elapsed}ms. Freed est: {Freed}",
            pruneResult.TotalRowsDeleted, pruneResult.ElapsedMs, pruneResult.FreedSizeFormatted);

        _lastPruneDateUtc = nowUtc.Date;
    }

    private async Task CheckAndExecuteBackupAsync(IDatabaseManagementService dbService, RetentionPolicyDto policies, DateTime nowUtc)
    {
        if (!policies.AutoBackupEnabled || nowUtc.Hour != policies.AutoBackupHourUtc || _lastBackupDateUtc?.Date == nowUtc.Date)
        {
            return;
        }

        _logger.LogInformation("Triggering scheduled database backup for {Date}", nowUtc.ToString("yyyy-MM-dd"));
        var backupItem = await dbService.CreateBackupAsync(new CreateBackupRequest
        {
            Label = "scheduled",
            Compress = true
        }, SystemUser, LocalIp, "Scheduled");

        _logger.LogInformation("Scheduled backup created: {FileName} ({Size})", backupItem.FileName, backupItem.SizeFormatted);

        await RotateScheduledBackupsAsync(dbService, policies.BackupRetentionCount);

        _lastBackupDateUtc = nowUtc.Date;
    }

    private async Task RotateScheduledBackupsAsync(IDatabaseManagementService dbService, int retentionCount)
    {
        var existingBackups = await dbService.GetBackupsAsync();
        var scheduledBackups = existingBackups
            .Where(b => b.Type == "Scheduled")
            .OrderByDescending(b => b.CreatedAt)
            .ToList();

        if (scheduledBackups.Count <= retentionCount)
        {
            return;
        }

        var toDelete = scheduledBackups.Skip(retentionCount).ToList();
        foreach (var oldBackup in toDelete)
        {
            _logger.LogInformation("Rotating old scheduled backup: {FileName}", oldBackup.FileName);
            await dbService.DeleteBackupAsync(oldBackup.FileName, SystemUser, LocalIp);
        }
    }
}

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

                // 1. Check Scheduled Auto-Prune
                if (policies.AutoPruneEnabled && 
                    nowUtc.Hour == policies.AutoPruneHourUtc && 
                    _lastPruneDateUtc?.Date != nowUtc.Date)
                {
                    _logger.LogInformation("Triggering scheduled retention pruning cycle for {Date}", nowUtc.ToString("yyyy-MM-dd"));
                    var pruneResult = await dbService.ExecutePruneAsync(new PruneRequest
                    {
                        Target = "all",
                        DryRun = false,
                        RunCheckpoint = true,
                        RunVacuum = false
                    }, "SystemScheduled", "127.0.0.1");

                    _logger.LogInformation("Scheduled pruning complete: {TotalRows} rows purged in {Elapsed}ms. Freed est: {Freed}",
                        pruneResult.TotalRowsDeleted, pruneResult.ElapsedMs, pruneResult.FreedSizeFormatted);

                    _lastPruneDateUtc = nowUtc.Date;
                }

                // 2. Check Scheduled Auto-Backup
                if (policies.AutoBackupEnabled && 
                    nowUtc.Hour == policies.AutoBackupHourUtc && 
                    _lastBackupDateUtc?.Date != nowUtc.Date)
                {
                    _logger.LogInformation("Triggering scheduled database backup for {Date}", nowUtc.ToString("yyyy-MM-dd"));
                    var backupItem = await dbService.CreateBackupAsync(new CreateBackupRequest
                    {
                        Label = "scheduled",
                        Compress = true
                    }, "SystemScheduled", "127.0.0.1", "Scheduled");

                    _logger.LogInformation("Scheduled backup created: {FileName} ({Size})", backupItem.FileName, backupItem.SizeFormatted);

                    // Backup rotation: prune oldest scheduled backups if exceeding count
                    var existingBackups = await dbService.GetBackupsAsync();
                    var scheduledBackups = existingBackups
                        .Where(b => b.Type == "Scheduled")
                        .OrderByDescending(b => b.CreatedAt)
                        .ToList();

                    if (scheduledBackups.Count > policies.BackupRetentionCount)
                    {
                        var toDelete = scheduledBackups.Skip(policies.BackupRetentionCount).ToList();
                        foreach (var oldBackup in toDelete)
                        {
                            _logger.LogInformation("Rotating old scheduled backup: {FileName}", oldBackup.FileName);
                            await dbService.DeleteBackupAsync(oldBackup.FileName, "SystemScheduled", "127.0.0.1");
                        }
                    }

                    _lastBackupDateUtc = nowUtc.Date;
                }
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
}


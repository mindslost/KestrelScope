using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using KestrelScope.Constants;
using KestrelScope.Models;
using KestrelScope.Services;

namespace KestrelScope.Controllers;

[ApiController]
[Route("api/admin/database")]
[Authorize(Roles = AppConstants.UserRoles.AdminNormalized)]
public class DatabaseAdminController : ControllerBase
{
    private readonly IDatabaseManagementService _dbService;

    public DatabaseAdminController(IDatabaseManagementService dbService)
    {
        _dbService = dbService;
    }

    private string CurrentUsername => User.Identity?.Name ?? AppConstants.UserRoles.AdminNormalized;
    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

    #region Storage & Diagnostics
    [HttpGet("storage")]
    public async Task<IActionResult> GetStorageStats()
    {
        var stats = await _dbService.GetStorageStatsAsync();
        return Ok(stats);
    }

    [HttpGet("health")]
    public async Task<IActionResult> CheckHealth()
    {
        var health = await _dbService.CheckHealthAsync();
        return Ok(health);
    }

    [HttpPost("vacuum")]
    public async Task<IActionResult> RunVacuum()
    {
        var msg = await _dbService.RunVacuumAsync(CurrentUsername, ClientIp);
        return Ok(new { status = "success", message = msg });
    }

    [HttpPost("checkpoint")]
    public async Task<IActionResult> RunCheckpoint()
    {
        var msg = await _dbService.RunWalCheckpointAsync(CurrentUsername, ClientIp);
        return Ok(new { status = "success", message = msg });
    }
    #endregion

    #region Retention Policies
    [HttpGet("retention")]
    public async Task<IActionResult> GetRetentionPolicies()
    {
        var policies = await _dbService.GetRetentionPoliciesAsync();
        return Ok(policies);
    }

    [HttpPut("retention")]
    public async Task<IActionResult> UpdateRetentionPolicies([FromBody] UpdateRetentionPolicyRequest req)
    {
        var updated = await _dbService.UpdateRetentionPoliciesAsync(req, CurrentUsername, ClientIp);
        return Ok(updated);
    }
    #endregion

    #region Pruning
    [HttpPost("prune")]
    public async Task<IActionResult> ExecutePrune([FromBody] PruneRequest req)
    {
        var result = await _dbService.ExecutePruneAsync(req, CurrentUsername, ClientIp);
        return Ok(result);
    }
    #endregion

    #region Backups
    [HttpGet("backups")]
    public async Task<IActionResult> GetBackups()
    {
        var backups = await _dbService.GetBackupsAsync();
        return Ok(backups);
    }

    [HttpPost("backups")]
    public async Task<IActionResult> CreateBackup([FromBody] CreateBackupRequest req)
    {
        var backup = await _dbService.CreateBackupAsync(req, CurrentUsername, ClientIp, "Manual");
        return Ok(backup);
    }

    [HttpGet("backups/{filename}/download")]
    public async Task<IActionResult> DownloadBackup(string filename)
    {
        try
        {
            var (stream, contentType, downloadName) = await _dbService.GetBackupDownloadStreamAsync(
                filename, CurrentUsername, ClientIp);
            return File(stream, contentType, downloadName);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = $"Backup file '{filename}' was not found." });
        }
    }

    [HttpDelete("backups/{filename}")]
    public async Task<IActionResult> DeleteBackup(string filename)
    {
        bool deleted = await _dbService.DeleteBackupAsync(filename, CurrentUsername, ClientIp);
        if (!deleted)
            return NotFound(new { error = $"Backup file '{filename}' was not found." });

        return Ok(new { status = "deleted", fileName = filename });
    }
    #endregion

    #region Restore & Disaster Recovery
    [HttpPost("restore")]
    public async Task<IActionResult> RestoreBackup([FromBody] RestoreRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.BackupFileName))
            return BadRequest(new { error = "BackupFileName is required." });

        if (!string.Equals(req.ConfirmationToken?.Trim(), AppConstants.DatabaseManagement.RestoreConfirmationKeyword, StringComparison.Ordinal))
        {
            return BadRequest(new { error = $"Confirmation token must match '{AppConstants.DatabaseManagement.RestoreConfirmationKeyword}'." });
        }

        try
        {
            var result = await _dbService.RestoreBackupAsync(req, CurrentUsername, ClientIp);
            return Ok(result);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = $"Restore operation failed: {ex.Message}" });
        }
    }
    #endregion

    #region Audit Logs
    [HttpGet("audit")]
    public async Task<IActionResult> GetAuditLogs([FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var logs = await _dbService.GetAuditLogsAsync(limit, offset);
        return Ok(logs);
    }
    #endregion
}

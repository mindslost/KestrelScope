using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using KestrelScope.Services;

namespace KestrelScope.Controllers;

#region DTOs & Models
public class UserRow
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "standard";
    public string? CreatedAt { get; set; }
}

public class UserDto
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Role { get; set; } = "standard";
    public string? CreatedAt { get; set; }
}

public record CreateUserRequest(string Username, string Password, string Role);
public record UpdateUserRequest(string? Role, string? Password);

public class AlertRuleDto
{
    public long? Id { get; set; }
    public string Name { get; set; } = "";
    public string MetricName { get; set; } = "";
    public double Threshold { get; set; }
    public int WindowMinutes { get; set; }
    public string WebhookUrl { get; set; } = "";
    public int? IsEnabled { get; set; } = 1;
}

public class MetricPoint
{
    public string Timestamp { get; set; } = "";
    public double Value { get; set; }
}

public class TraceSpanDto
{
    public string TraceId { get; set; } = "";
    public string SpanId { get; set; } = "";
    public string? ParentSpanId { get; set; }
    public string ServiceName { get; set; } = "";
    public string SpanName { get; set; } = "";
    public double DurationMs { get; set; }
    public string StatusCode { get; set; } = "";
    public string Timestamp { get; set; } = "";
}

public class LogRecordDto
{
    public long Id { get; set; }
    public string Timestamp { get; set; } = "";
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string ServiceName { get; set; } = "";
    public string SeverityText { get; set; } = "";
    public int SeverityNumber { get; set; }
    public string Body { get; set; } = "";
    public string? AttributesJson { get; set; }
}
#endregion

#region Auth Controller (/api/auth)
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly string _dbConn;

    public AuthController(IConfiguration config)
    {
        _dbConn = config.GetConnectionString("DefaultConnection") ?? "Data Source=observability.db;";
    }

    public record LoginRequest(string Username, string Password);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Username and password are required." });

        using var conn = new SqliteConnection(_dbConn);
        await conn.OpenAsync();

        var user = await conn.QuerySingleOrDefaultAsync<UserRow>(
            "SELECT Id, Username, PasswordHash, Role, CreatedAt FROM Users WHERE Username = @Username;",
            new { req.Username }
        );

        if (user == null || !PasswordHasher.VerifyPassword(req.Password, user.PasswordHash))
        {
            return Unauthorized(new { error = "Invalid credentials." });
        }

        string normalizedRole = string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase)
            ? "Admin"
            : "Standard";

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, normalizedRole),
            new("UserId", user.Id.ToString())
        };

        var identity = new ClaimsIdentity(claims, "CookieAuth");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("CookieAuth", principal, new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
        });

        return Ok(new { status = "success", username = user.Username, role = normalizedRole });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("CookieAuth");
        return Ok(new { status = "logged_out" });
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return Ok(new
            {
                username = User.Identity.Name,
                role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Standard",
                isAuthenticated = true
            });
        }

        return Ok(new
        {
            username = (string?)null,
            role = (string?)null,
            isAuthenticated = false
        });
    }
}
#endregion

#region Metrics Controller (/api/metrics)
[ApiController]
[Route("api/metrics")]
public class MetricsController : ControllerBase
{
    private readonly string _dbConn;

    public MetricsController(IConfiguration config)
    {
        _dbConn = config.GetConnectionString("DefaultConnection") ?? "Data Source=observability.db;";
    }

    [HttpGet("services")]
    public async Task<IActionResult> GetServices()
    {
        using var conn = new SqliteConnection(_dbConn);
        var services = await conn.QueryAsync<string>(
            "SELECT DISTINCT ServiceName FROM MetricSamples ORDER BY ServiceName ASC;"
        );
        return Ok(services);
    }

    [HttpGet("names")]
    public async Task<IActionResult> GetMetricNames([FromQuery] string? service)
    {
        using var conn = new SqliteConnection(_dbConn);
        string sql = string.IsNullOrWhiteSpace(service)
            ? "SELECT DISTINCT MetricName FROM MetricSamples ORDER BY MetricName ASC;"
            : "SELECT DISTINCT MetricName FROM MetricSamples WHERE ServiceName = @service ORDER BY MetricName ASC;";

        var names = await conn.QueryAsync<string>(sql, new { service });
        return Ok(names);
    }

    [HttpGet("series")]
    public async Task<IActionResult> GetSeries(
        [FromQuery] string service,
        [FromQuery] string metric,
        [FromQuery] int minutes = 60)
    {
        if (string.IsNullOrWhiteSpace(service) || string.IsNullOrWhiteSpace(metric))
            return BadRequest(new { error = "Service and metric query parameters are required." });

        if (minutes <= 0) minutes = 60;
        var windowStart = DateTime.UtcNow.AddMinutes(-minutes).ToString("o");

        using var conn = new SqliteConnection(_dbConn);
        var series = await conn.QueryAsync<MetricPoint>(
            @"SELECT Timestamp, Value 
              FROM MetricSamples 
              WHERE ServiceName = @service AND MetricName = @metric AND Timestamp >= @windowStart
              ORDER BY Timestamp ASC;",
            new { service, metric, windowStart }
        );

        return Ok(series);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        using var conn = new SqliteConnection(_dbConn);
        var windowStart24h = DateTime.UtcNow.AddHours(-24).ToString("o");

        int serviceCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT ServiceName) FROM MetricSamples;"
        );

        long sampleCount24h = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM MetricSamples WHERE Timestamp >= @windowStart24h;",
            new { windowStart24h }
        );

        int activeAlerts = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AlertRules WHERE IsEnabled = 1;"
        );

        long traceCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM Traces;"
        );

        return Ok(new
        {
            serviceCount,
            sampleCount24h,
            activeAlerts,
            traceCount
        });
    }
}
#endregion

#region Alerts Controller (/api/alerts)
[ApiController]
[Route("api/alerts")]
public class AlertsController : ControllerBase
{
    private readonly string _dbConn;

    public AlertsController(IConfiguration config)
    {
        _dbConn = config.GetConnectionString("DefaultConnection") ?? "Data Source=observability.db;";
    }

    [HttpGet]
    public async Task<IActionResult> GetAlertRules()
    {
        using var conn = new SqliteConnection(_dbConn);
        var rules = await conn.QueryAsync<AlertRuleDto>(
            "SELECT Id, Name, MetricName, Threshold, WindowMinutes, WebhookUrl, IsEnabled FROM AlertRules ORDER BY Id DESC;"
        );
        return Ok(rules);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SaveRule([FromBody] AlertRuleDto rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Name) || string.IsNullOrWhiteSpace(rule.MetricName) || string.IsNullOrWhiteSpace(rule.WebhookUrl))
            return BadRequest(new { error = "Name, MetricName, and WebhookUrl are required." });

        if (rule.WindowMinutes <= 0)
            return BadRequest(new { error = "WindowMinutes must be greater than 0." });

        using var conn = new SqliteConnection(_dbConn);

        if (rule.Id.HasValue && rule.Id.Value > 0)
        {
            await conn.ExecuteAsync(
                @"UPDATE AlertRules 
                  SET Name = @Name, MetricName = @MetricName, Threshold = @Threshold, 
                      WindowMinutes = @WindowMinutes, WebhookUrl = @WebhookUrl, IsEnabled = COALESCE(@IsEnabled, IsEnabled)
                  WHERE Id = @Id;",
                rule
            );
            return Ok(new { status = "updated", id = rule.Id.Value });
        }
        else
        {
            long newId = await conn.QuerySingleAsync<long>(
                @"INSERT INTO AlertRules (Name, MetricName, Threshold, WindowMinutes, WebhookUrl, IsEnabled)
                  VALUES (@Name, @MetricName, @Threshold, @WindowMinutes, @WebhookUrl, COALESCE(@IsEnabled, 1));
                  SELECT last_insert_rowid();",
                rule
            );
            return Ok(new { status = "created", id = newId });
        }
    }

    [HttpPatch("{id}/toggle")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ToggleRule(long id)
    {
        using var conn = new SqliteConnection(_dbConn);
        int affected = await conn.ExecuteAsync(
            "UPDATE AlertRules SET IsEnabled = CASE WHEN IsEnabled = 1 THEN 0 ELSE 1 END WHERE Id = @id;",
            new { id }
        );

        if (affected == 0)
            return NotFound(new { error = $"Alert rule #{id} not found." });

        int isEnabled = await conn.ExecuteScalarAsync<int>(
            "SELECT IsEnabled FROM AlertRules WHERE Id = @id;",
            new { id }
        );

        return Ok(new { status = "toggled", id, isEnabled = isEnabled == 1 });
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteRule(long id)
    {
        using var conn = new SqliteConnection(_dbConn);
        int affected = await conn.ExecuteAsync("DELETE FROM AlertRules WHERE Id = @id;", new { id });
        if (affected == 0)
            return NotFound(new { error = $"Alert rule #{id} not found." });

        return Ok(new { status = "deleted", id });
    }
}
#endregion

#region Users Controller (/api/users)
[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public class UsersController : ControllerBase
{
    private readonly string _dbConn;

    public UsersController(IConfiguration config)
    {
        _dbConn = config.GetConnectionString("DefaultConnection") ?? "Data Source=observability.db;";
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        using var conn = new SqliteConnection(_dbConn);
        var users = await conn.QueryAsync<UserDto>(
            "SELECT Id, Username, Role, CreatedAt FROM Users ORDER BY Id ASC;"
        );
        return Ok(users);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Username and password are required." });

        string role = string.Equals(req.Role, "admin", StringComparison.OrdinalIgnoreCase) ? "admin" : "standard";

        using var conn = new SqliteConnection(_dbConn);
        await conn.OpenAsync();

        var existing = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Users WHERE LOWER(Username) = LOWER(@Username);",
            new { req.Username }
        );
        if (existing > 0)
            return BadRequest(new { error = $"User '{req.Username}' already exists." });

        string passwordHash = PasswordHasher.HashPassword(req.Password);

        long newId = await conn.QuerySingleAsync<long>(
            @"INSERT INTO Users (Username, PasswordHash, Role) 
              VALUES (@Username, @PasswordHash, @Role);
              SELECT last_insert_rowid();",
            new { req.Username, PasswordHash = passwordHash, Role = role }
        );

        var created = await conn.QuerySingleAsync<UserDto>(
            "SELECT Id, Username, Role, CreatedAt FROM Users WHERE Id = @newId;",
            new { newId }
        );

        return Ok(created);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateUser(long id, [FromBody] UpdateUserRequest req)
    {
        using var conn = new SqliteConnection(_dbConn);
        await conn.OpenAsync();

        var user = await conn.QuerySingleOrDefaultAsync<UserRow>(
            "SELECT Id, Username, PasswordHash, Role, CreatedAt FROM Users WHERE Id = @id;",
            new { id }
        );
        if (user == null)
            return NotFound(new { error = $"User #{id} not found." });

        string targetRole = user.Role;
        if (!string.IsNullOrWhiteSpace(req.Role))
        {
            targetRole = string.Equals(req.Role, "admin", StringComparison.OrdinalIgnoreCase) ? "admin" : "standard";
            if (string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(targetRole, "admin", StringComparison.OrdinalIgnoreCase))
            {
                int adminCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM Users WHERE LOWER(Role) = 'admin';"
                );
                if (adminCount <= 1)
                {
                    return BadRequest(new { error = "Cannot demote the last remaining administrator." });
                }
            }
        }

        string passwordHash = user.PasswordHash;
        if (!string.IsNullOrWhiteSpace(req.Password))
        {
            passwordHash = PasswordHasher.HashPassword(req.Password);
        }

        await conn.ExecuteAsync(
            @"UPDATE Users 
              SET Role = @Role, PasswordHash = @PasswordHash 
              WHERE Id = @id;",
            new { Role = targetRole, PasswordHash = passwordHash, id }
        );

        var updated = await conn.QuerySingleAsync<UserDto>(
            "SELECT Id, Username, Role, CreatedAt FROM Users WHERE Id = @id;",
            new { id }
        );

        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(long id)
    {
        using var conn = new SqliteConnection(_dbConn);
        await conn.OpenAsync();

        var user = await conn.QuerySingleOrDefaultAsync<UserRow>(
            "SELECT Id, Username, PasswordHash, Role, CreatedAt FROM Users WHERE Id = @id;",
            new { id }
        );
        if (user == null)
            return NotFound(new { error = $"User #{id} not found." });

        var currentUserIdStr = User.FindFirst("UserId")?.Value;
        if (long.TryParse(currentUserIdStr, out long currentUserId) && currentUserId == id)
        {
            return BadRequest(new { error = "You cannot delete your own account." });
        }

        if (string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            int adminCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Users WHERE LOWER(Role) = 'admin';"
            );
            if (adminCount <= 1)
            {
                return BadRequest(new { error = "Cannot delete the last remaining administrator." });
            }
        }

        await conn.ExecuteAsync("DELETE FROM Users WHERE Id = @id;", new { id });
        return Ok(new { status = "deleted", id });
    }
}
#endregion

#region Traces Controller (/api/traces)
[ApiController]
[Route("api/traces")]
public class TracesController : ControllerBase
{
    private readonly string _dbConn;

    public TracesController(IConfiguration config)
    {
        _dbConn = config.GetConnectionString("DefaultConnection") ?? "Data Source=observability.db;";
    }

    [HttpGet]
    public async Task<IActionResult> GetTraces(
        [FromQuery] string? service,
        [FromQuery] string? traceId,
        [FromQuery] int minutes = 60,
        [FromQuery] int limit = 100)
    {
        if (minutes <= 0) minutes = 60;
        if (limit <= 0 || limit > 500) limit = 100;
        var windowStart = DateTime.UtcNow.AddMinutes(-minutes).ToString("o");

        using var conn = new SqliteConnection(_dbConn);

        string sql = @"
            SELECT TraceId, SpanId, ParentSpanId, ServiceName, SpanName, DurationMs, StatusCode, Timestamp
            FROM Traces
            WHERE ((@traceId IS NOT NULL AND TraceId = @traceId) OR (@traceId IS NULL AND Timestamp >= @windowStart))
              AND (@service IS NULL OR ServiceName = @service)
            ORDER BY Timestamp DESC
            LIMIT @limit;";

        var spans = await conn.QueryAsync<TraceSpanDto>(sql, new { windowStart, service, traceId, limit });
        return Ok(spans);
    }

    [HttpGet("{traceId}")]
    public async Task<IActionResult> GetTraceSpans(string traceId)
    {
        using var conn = new SqliteConnection(_dbConn);
        var spans = await conn.QueryAsync<TraceSpanDto>(
            @"SELECT TraceId, SpanId, ParentSpanId, ServiceName, SpanName, DurationMs, StatusCode, Timestamp
              FROM Traces
              WHERE TraceId = @traceId
              ORDER BY Timestamp ASC, DurationMs DESC;",
            new { traceId }
        );

        return Ok(spans);
    }
}
#endregion

#region Logs Controller (/api/logs)
[ApiController]
[Route("api/logs")]
public class LogsController : ControllerBase
{
    private readonly string _dbConn;

    public LogsController(IConfiguration config)
    {
        _dbConn = config.GetConnectionString("DefaultConnection") ?? "Data Source=observability.db;";
    }

    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? service,
        [FromQuery] string? severity,
        [FromQuery] string? traceId,
        [FromQuery] string? query,
        [FromQuery] int minutes = 60,
        [FromQuery] int limit = 100)
    {
        if (minutes <= 0) minutes = 60;
        if (limit <= 0 || limit > 500) limit = 100;
        var windowStart = DateTime.UtcNow.AddMinutes(-minutes).ToString("o");

        using var conn = new SqliteConnection(_dbConn);

        string sql = @"
            SELECT Id, Timestamp, TraceId, SpanId, ServiceName, SeverityText, SeverityNumber, Body, AttributesJson
            FROM Logs
            WHERE Timestamp >= @windowStart
              AND (@service IS NULL OR @service = '' OR ServiceName = @service)
              AND (@severity IS NULL OR @severity = '' OR SeverityText = @severity)
              AND (@traceId IS NULL OR @traceId = '' OR TraceId = @traceId)
              AND (@query IS NULL OR @query = '' OR Body LIKE @likeQuery)
            ORDER BY Timestamp DESC
            LIMIT @limit;";

        string? likeQuery = string.IsNullOrWhiteSpace(query) ? null : $"%{query.Trim()}%";

        var logs = await conn.QueryAsync<LogRecordDto>(sql, new
        {
            windowStart,
            service = string.IsNullOrWhiteSpace(service) ? null : service.Trim(),
            severity = string.IsNullOrWhiteSpace(severity) ? null : severity.Trim().ToUpperInvariant(),
            traceId = string.IsNullOrWhiteSpace(traceId) ? null : traceId.Trim(),
            query = string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
            likeQuery,
            limit
        });

        return Ok(logs);
    }

    [HttpGet("services")]
    public async Task<IActionResult> GetLogServices()
    {
        using var conn = new SqliteConnection(_dbConn);
        var services = await conn.QueryAsync<string>(
            "SELECT DISTINCT ServiceName FROM Logs ORDER BY ServiceName ASC;"
        );
        return Ok(services);
    }
}
#endregion



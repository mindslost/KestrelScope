using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KestrelScope.Constants;

namespace KestrelScope.Services;

public class AlertRulerWorker : BackgroundService
{
    private readonly string _dbConn;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AlertRulerWorker> _logger;
    private readonly ConcurrentDictionary<(int RuleId, string ServiceName), DateTime> _activeAlerts = new();

    public AlertRulerWorker(IConfiguration config, HttpClient httpClient, ILogger<AlertRulerWorker> logger)
    {
        _dbConn = config.GetConnectionString("DefaultConnection") ?? AppConstants.Database.DefaultConnectionString;
        _httpClient = httpClient;
        _logger = logger;
    }

    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromSeconds(AppConstants.Alerts.DefaultEvaluationIntervalSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AlertRulerWorker started with evaluation interval {Interval}.", EvaluationInterval);

        // Allow web host to finish binding ports before first background evaluation
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(AppConstants.Alerts.InitialDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateAlertRulesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating alert rules.");
            }

            try
            {
                await Task.Delay(EvaluationInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<int> EvaluateAlertRulesAsync(CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_dbConn);
        await connection.OpenAsync(ct);

        var rules = await FetchActiveRulesAsync(connection, ct);
        int alertsTriggered = 0;
        var currentCycleBreaches = new HashSet<(int RuleId, string ServiceName)>();

        foreach (var rule in rules)
        {
            alertsTriggered += await EvaluateRuleAsync(connection, rule, currentCycleBreaches, ct);
        }

        await ResolveNormalizedAlertsAsync(rules, currentCycleBreaches, ct);

        return alertsTriggered;
    }

    private static async Task<List<(int Id, string Name, string Metric, double Threshold, int Window, string Webhook)>> FetchActiveRulesAsync(
        SqliteConnection connection, CancellationToken ct)
    {
        var rules = new List<(int Id, string Name, string Metric, double Threshold, int Window, string Webhook)>();
        using var ruleCmd = connection.CreateCommand();
        ruleCmd.CommandText = "SELECT Id, Name, MetricName, Threshold, WindowMinutes, WebhookUrl FROM AlertRules WHERE IsEnabled = 1;";
        using var reader = await ruleCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rules.Add((
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetInt32(4),
                reader.GetString(5)
            ));
        }
        return rules;
    }

    private async Task<int> EvaluateRuleAsync(
        SqliteConnection connection,
        (int Id, string Name, string Metric, double Threshold, int Window, string Webhook) rule,
        HashSet<(int RuleId, string ServiceName)> currentCycleBreaches,
        CancellationToken ct)
    {
        int newlyTriggered = 0;
        using var evalCmd = connection.CreateCommand();
        evalCmd.CommandText = @"
            SELECT ServiceName, AVG(Value) as AvgValue 
            FROM MetricSamples 
            WHERE MetricName = @metric AND Timestamp >= @windowStart 
            GROUP BY ServiceName 
            HAVING AvgValue > @threshold;";

        evalCmd.Parameters.AddWithValue("@metric", rule.Metric);
        evalCmd.Parameters.AddWithValue("@windowStart", DateTime.UtcNow.AddMinutes(-rule.Window).ToString("o"));
        evalCmd.Parameters.AddWithValue("@threshold", rule.Threshold);

        using var evalReader = await evalCmd.ExecuteReaderAsync(ct);
        while (await evalReader.ReadAsync(ct))
        {
            string service = evalReader.GetString(0);
            double avgValue = evalReader.GetDouble(1);
            var alertKey = (rule.Id, service);
            currentCycleBreaches.Add(alertKey);

            if (_activeAlerts.TryAdd(alertKey, DateTime.UtcNow))
            {
                newlyTriggered++;
                _logger.LogWarning("ALERT TRIGGERED [FIRING]: Rule '{Rule}' breached by {Service}. Avg Value: {Val:F2} > Threshold: {Threshold}",
                    rule.Name, service, avgValue, rule.Threshold);

                await DispatchWebhookAsync(rule, service, avgValue, AppConstants.Alerts.StateFiring, ct);
            }
            else
            {
                _logger.LogDebug("Alert '{Rule}' for {Service} continues to breach ({Val:F2}), notification suppressed (already FIRING).",
                    rule.Name, service, avgValue);
            }
        }

        return newlyTriggered;
    }

    private async Task ResolveNormalizedAlertsAsync(
        List<(int Id, string Name, string Metric, double Threshold, int Window, string Webhook)> rules,
        HashSet<(int RuleId, string ServiceName)> currentCycleBreaches,
        CancellationToken ct)
    {
        foreach (var activeKey in _activeAlerts.Keys)
        {
            if (currentCycleBreaches.Contains(activeKey)) continue;
            if (!_activeAlerts.TryRemove(activeKey, out _)) continue;

            var resolvedRule = rules.Find(r => r.Id == activeKey.RuleId);
            string ruleName = resolvedRule != default ? resolvedRule.Name : $"Rule #{activeKey.RuleId}";
            _logger.LogInformation("ALERT RESOLVED: Rule '{Rule}' normalized for service {Service}.", ruleName, activeKey.ServiceName);

            if (resolvedRule != default)
            {
                await DispatchWebhookAsync(resolvedRule, activeKey.ServiceName, 0.0, AppConstants.Alerts.StateResolved, ct);
            }
        }
    }

    private async Task DispatchWebhookAsync((int Id, string Name, string Metric, double Threshold, int Window, string Webhook) rule, string service, double avgValue, string state, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rule.Webhook))
            return;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(AppConstants.Alerts.WebhookTimeoutSeconds));

            var payload = JsonSerializer.Serialize(new
            {
                alert = rule.Name,
                service = service,
                metric = rule.Metric,
                currentValue = avgValue,
                threshold = rule.Threshold,
                windowMinutes = rule.Window,
                state = state,
                timestamp = DateTime.UtcNow
            });

            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(rule.Webhook, content, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Dispatched {State} alert webhook for '{Rule}' to {Url} (Status: {StatusCode}).",
                    state, rule.Name, rule.Webhook, response.StatusCode);
            }
            else
            {
                _logger.LogWarning("Webhook for '{Rule}' to {Url} returned non-success status: {StatusCode}.",
                    rule.Name, rule.Webhook, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch webhook for alert '{Rule}' to {Url}.", rule.Name, rule.Webhook);
        }
    }
}

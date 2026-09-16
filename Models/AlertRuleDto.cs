namespace KestrelScope.Models;

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


namespace KestrelScope.Models;

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


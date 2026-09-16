namespace KestrelScope.Models;

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


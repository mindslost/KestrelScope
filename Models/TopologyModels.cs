namespace KestrelScope.Models;

public class TopologyNodeDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = "service"; // "service", "database", "queue"
    public int NodeCount { get; set; } = 1;
    public string Health { get; set; } = "normal"; // "normal", "warning", "critical"
    public double CallsPerMin { get; set; }
    public double AvgLatencyMs { get; set; }
    public double ErrorRatePercent { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public string? TechBadge { get; set; }
}

public class TopologyEdgeDto
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public double CallsPerMin { get; set; }
    public double AvgLatencyMs { get; set; }
    public double ErrorRatePercent { get; set; }
    public string Protocol { get; set; } = "HTTP";
    public bool IsAsync { get; set; } = false;
    public string Health { get; set; } = "normal";
}

public class TopologyScorecardDto
{
    public double NormalPercent { get; set; } = 98.4;
    public double SlowPercent { get; set; } = 1.1;
    public double VerySlowPercent { get; set; } = 0.3;
    public double StallPercent { get; set; } = 0.0;
    public double ErrorPercent { get; set; } = 0.2;

    public int NodesNormal { get; set; }
    public int NodesWarning { get; set; }
    public int NodesCritical { get; set; }

    public long TotalCalls { get; set; }
    public double CallsPerMin { get; set; }
    public double AvgLatencyMs { get; set; }
    public long TotalExceptions { get; set; }
    public double ErrorsPerMin { get; set; }
}

public class TopologyTimeSeriesPoint
{
    public string Timestamp { get; set; } = string.Empty;
    public double CallsPerMin { get; set; }
    public double AvgLatencyMs { get; set; }
    public double ErrorRatePercent { get; set; }
}

public class TopologyFlowMapResponse
{
    public List<TopologyNodeDto> Nodes { get; set; } = new();
    public List<TopologyEdgeDto> Edges { get; set; } = new();
    public TopologyScorecardDto Scorecard { get; set; } = new();
    public List<TopologyTimeSeriesPoint> TimeSeries { get; set; } = new();
    public string ApplicationName { get; set; } = "ECommerce";
    public int WindowMinutes { get; set; } = 15;
}


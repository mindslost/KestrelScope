namespace KestrelScope.Models;

/// <summary>
/// User role types supported by KestrelScope.
/// </summary>
public enum UserRole
{
    Standard = 0,
    Admin = 1
}

/// <summary>
/// OpenTelemetry Span Status Codes.
/// </summary>
public enum SpanStatusCode
{
    Unset = 0,
    Ok = 1,
    Error = 2
}

/// <summary>
/// OpenTelemetry standard severity numbers for structured logs.
/// </summary>
public enum OtelSeverity
{
    Trace = 1,
    Debug = 5,
    Info = 9,
    Warn = 13,
    Error = 17,
    Fatal = 21
}

/// <summary>
/// AppDynamics topology node types.
/// </summary>
public enum TopologyNodeType
{
    Service,
    Database,
    Queue
}

/// <summary>
/// AppDynamics node and transaction health status.
/// </summary>
public enum TopologyHealthStatus
{
    Normal,
    Warning,
    Critical
}


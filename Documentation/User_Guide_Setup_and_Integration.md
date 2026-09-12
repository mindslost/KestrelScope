# KestrelScope — User Setup & Integration Guide

A complete operational guide for deploying **KestrelScope** and integrating it with any microservice, application, or existing telemetry pipeline using standard OpenTelemetry (OTLP/HTTP) protocols.

---

## Table of Contents
1. [Platform Overview & Architecture](#1-platform-overview--architecture)
2. [Prerequisites & System Requirements](#2-prerequisites--system-requirements)
3. [Deployment Options](#3-deployment-options)
   - [Option A: Bare-Metal / Host Deployment (.NET 9)](#option-a-bare-metal--host-deployment-net-9)
   - [Option B: Container Deployment (Docker & Docker Compose)](#option-b-container-deployment-docker--docker-compose)
   - [Option C: Linux Systemd Daemon](#option-c-linux-systemd-daemon)
4. [Service Integration Guide](#4-service-integration-guide)
   - [Standard OpenTelemetry Protocol Configuration](#standard-opentelemetry-protocol-configuration)
   - [.NET 8 / 9 Service Integration](#1-net-8--9-service-integration)
   - [Node.js / Express Service Integration](#2-nodejs--express-service-integration)
   - [Python / FastAPI Service Integration](#3-python--fastapi-service-integration)
   - [OpenTelemetry Collector (Sidecar / Gateway) Integration](#4-opentelemetry-collector-sidecar--gateway-integration)
   - [Raw HTTP / cURL Ingestion](#5-raw-http--curl-ingestion)
5. [Using the Web Console](#5-using-the-web-console)
   - [Metrics Explorer](#metrics-explorer)
   - [Traces Explorer & Waterfall View](#traces-explorer--waterfall-view)
   - [Logs Explorer & Trace Correlation](#logs-explorer--trace-correlation)
   - [Alert Rules & Webhooks](#alert-rules--webhooks)
6. [Database Management & Maintenance](#6-database-management--maintenance)
7. [Troubleshooting & Verification](#7-troubleshooting--verification)

---

## 1. Platform Overview & Architecture

KestrelScope is an **air-gapped, sovereign, single-binary observability monolith** engineered in .NET 9. It provides complete observability across the three core telemetry pillars without requiring external databases (no ClickHouse, PostgreSQL, or Redis), third-party SaaS agents, or external CDN dependencies.

```
                     ┌──────────────────────────────────────────────┐
                     │            Monitored Microservices           │
                     │  (.NET / Node.js / Python / Go / Collector)  │
                     └──────┬────────────────┬───────────────┬──────┘
                            │                │               │
                     OTLP   │          OTLP  │         OTLP  │
                    Metrics │         Traces │          Logs │
                            ▼                ▼               ▼
┌───────────────────────────────────────────────────────────────────────────┐
│                           KestrelScope Monolith                           │
│                                                                           │
│  ┌─────────────────────── OTLP Ingestion Engine ───────────────────────┐  │
│  │   POST /v1/metrics         POST /v1/traces         POST /v1/logs    │  │
│  └──────────┬──────────────────────┬──────────────────────┬────────────┘  │
│             │                      │                      │               │
│             ▼                      ▼                      ▼               │
│  ┌────────────────── Embedded SQLite Database (WAL Mode) ──────────────┐  │
│  │   MetricSamples           Spans & Events          Structured Logs   │  │
│  │   AlertRules              AlertIncidents          Users & Sessions  │  │
│  └─────────────────────────────────┬───────────────────────────────────┘  │
│                                    │                                      │
│  ┌─────────────────────── Background Services ─────────────────────────┐  │
│  │   • AlertRuleWorker (Automated metric evaluation & webhook dispatch)│  │
│  └─────────────────────────────────┬───────────────────────────────────┘  │
│                                    │                                      │
│  ┌────────────────────── Fluent 2 Web Console ─────────────────────────┐  │
│  │   • Metrics Explorer (Chart.js)    • Distributed Traces Waterfall   │  │
│  │   • Structured Logs Explorer       • Alert Rules Management         │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────────────────────────────────────────────────┘
```

### Key Ingestion Endpoints (Standard OTLP/HTTP)
| Endpoint | Method | Format | Description |
| :--- | :--- | :--- | :--- |
| `/v1/metrics` | `POST` | JSON / OTLP | Ingests metric data points, gauges, and counters |
| `/v1/traces` | `POST` | JSON / OTLP | Ingests distributed trace spans and parent-child hierarchies |
| `/v1/logs` | `POST` | JSON / OTLP | Ingests structured log events with correlated Trace/Span IDs |
| `/health` | `GET` | JSON | Service health status check |

---

## 2. Prerequisites & System Requirements

- **Supported Platforms**: Linux (x64/arm64), macOS (Apple Silicon/Intel), Windows 10/11 / Server.
- **Bare-Metal Runtime**: [.NET 9.0 SDK or Runtime](https://dotnet.microsoft.com/download/dotnet/9.0).
- **Container Runtime**: Docker CE 20.10+ or Podman.
- **Port Requirements**: Port `5000` (default HTTP listener).
- **Storage**: SSD recommended for high SQLite Write-Ahead Logging (WAL) throughput.

---

## 3. Deployment Options

### Option A: Bare-Metal / Host Deployment (.NET 9)

1. **Clone and Build**:
   ```bash
   git clone https://github.com/mindslost/KestrelScope.git
   cd KestrelScope
   dotnet build -c Release
   ```

2. **Run KestrelScope**:
   ```bash
   # Run directly with default port 5000
   dotnet run -c Release --urls "http://0.0.0.0:5000"
   ```

3. **Publish as a Self-Contained Single Binary (Optional)**:
   ```bash
   dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
   ./publish/KestrelScope --urls "http://0.0.0.0:5000"
   ```

4. **Verify Health**:
   ```bash
   curl -s http://localhost:5000/health
   # Response: {"status":"Healthy","timestamp":"..."}
   ```

5. **Access the Web Console**:
   Navigate to `http://localhost:5000/` in your browser.
   - **Default Username**: `admin`
   - **Default Password**: `admin123`

---

### Option B: Container Deployment (Docker & Docker Compose)

KestrelScope includes ready-to-use Docker orchestration with SQLite host-mounted volumes for persistent data.

1. **Start via Docker Compose**:
   ```bash
   docker compose up -d kestrelscope
   ```

2. **Verify Container Status**:
   ```bash
   docker compose ps
   docker compose logs -f kestrelscope
   ```

3. **Standalone `docker run` Command**:
   ```bash
   docker build -t kestrelscope:latest .
   docker run -d \
     --name kestrelscope \
     -p 5000:5000 \
     -v $(pwd)/data:/app/host:Z \
     -e ConnectionStrings__DefaultConnection="Data Source=/app/host/observability.db;" \
     --restart unless-stopped \
     kestrelscope:latest
   ```

---

### Option C: Linux Systemd Daemon

For continuous production operation on a Linux server:

1. Create a service user and directory:
   ```bash
   sudo useradd -rs /bin/false kestrelscope
   sudo mkdir -p /var/lib/kestrelscope
   sudo chown -R kestrelscope:kestrelscope /var/lib/kestrelscope
   ```

2. Create `/etc/systemd/system/kestrelscope.service`:
   ```ini
   [Unit]
   Description=KestrelScope Observability Monolith
   After=network.target

   [Service]
   WorkingDirectory=/var/lib/kestrelscope
   ExecStart=/usr/bin/dotnet /var/lib/kestrelscope/KestrelScope.dll --urls "http://0.0.0.0:5000"
   Restart=always
   RestartSec=5
   SyslogIdentifier=kestrelscope
   User=kestrelscope
   Environment=ASPNETCORE_ENVIRONMENT=Production
   Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
   Environment=ConnectionStrings__DefaultConnection=Data Source=/var/lib/kestrelscope/observability.db;

   [Install]
   WantedBy=multi-user.target
   ```

3. Enable and start:
   ```bash
   sudo systemctl daemon-reload
   sudo systemctl enable --now kestrelscope
   sudo systemctl status kestrelscope
   ```

---

## 4. Service Integration Guide

Any service can send telemetry to KestrelScope using standard OpenTelemetry SDKs or simple HTTP requests.

### Standard OpenTelemetry Protocol Configuration
Regardless of the programming language, set these two standard environment variables on your application:

```bash
# Target the KestrelScope HTTP OTLP receiver
export OTEL_EXPORTER_OTLP_ENDPOINT="http://<KESTRELSCOPE_HOST>:5000"

# Specify the OTLP/HTTP protocol
export OTEL_EXPORTER_OTLP_PROTOCOL="http/protobuf"   # or "http/json"

# Provide your service identifier
export OTEL_SERVICE_NAME="my-microservice"
```

---

### 1. .NET 8 / 9 Service Integration

#### Step 1: Install NuGet Packages
In your .NET microservice project:
```bash
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Instrumentation.Http
```

#### Step 2: Configure `Program.cs`
Add standard OpenTelemetry tracing, metrics, and structured logging:

```csharp
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// 1. Identify service name and KestrelScope endpoint
string serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? "order-service";
string kestrelScopeUrl = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:5000";

var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(serviceName: serviceName, serviceVersion: "1.0.0");

// 2. Configure Distributed Tracing & Metrics
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(resourceBuilder)
        .AddAspNetCoreInstrumentation(opts => opts.RecordException = true)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(opts =>
        {
            opts.Endpoint = new Uri($"{kestrelScopeUrl}/v1/traces");
            opts.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf; // or HttpJson
        }))
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(resourceBuilder)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(opts =>
        {
            opts.Endpoint = new Uri($"{kestrelScopeUrl}/v1/metrics");
            opts.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
        }));

// 3. Configure Structured Logging with Trace Correlation
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.SetResourceBuilder(resourceBuilder);
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.AddOtlpExporter(opts =>
    {
        opts.Endpoint = new Uri($"{kestrelScopeUrl}/v1/logs");
        opts.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
    });
});

var app = builder.Build();

app.MapGet("/api/checkout", (ILogger<Program> logger) =>
{
    logger.LogInformation("Processing customer checkout");
    return Results.Ok(new { status = "Success" });
});

app.Run();
```

---

### 2. Node.js / Express Service Integration

#### Step 1: Install Dependencies
```bash
npm install @opentelemetry/sdk-node \
            @opentelemetry/auto-instrumentations-node \
            @opentelemetry/exporter-trace-otlp-http \
            @opentelemetry/exporter-metrics-otlp-http \
            @opentelemetry/resources \
            @opentelemetry/semantic-conventions
```

#### Step 2: Create Telemetry Initializer (`tracer.js`)
```javascript
const { NodeSDK } = require('@opentelemetry/sdk-node');
const { getNodeAutoInstrumentations } = require('@opentelemetry/auto-instrumentations-node');
const { OTLPTraceExporter } = require('@opentelemetry/exporter-trace-otlp-http');
const { OTLPMetricExporter } = require('@opentelemetry/exporter-metrics-otlp-http');
const { PeriodicExportingMetricReader } = require('@opentelemetry/sdk-metrics');
const { Resource } = require('@opentelemetry/resources');
const { SemanticResourceAttributes } = require('@opentelemetry/semantic-conventions');

const endpoint = process.env.OTEL_EXPORTER_OTLP_ENDPOINT || 'http://localhost:5000';
const serviceName = process.env.OTEL_SERVICE_NAME || 'node-api-service';

const sdk = new NodeSDK({
  resource: new Resource({
    [SemanticResourceAttributes.SERVICE_NAME]: serviceName,
  }),
  traceExporter: new OTLPTraceExporter({
    url: `${endpoint}/v1/traces`,
  }),
  metricReader: new PeriodicExportingMetricReader({
    exporter: new OTLPMetricExporter({
      url: `${endpoint}/v1/metrics`,
    }),
    exportIntervalMillis: 15000,
  }),
  instrumentations: [getNodeAutoInstrumentations()],
});

sdk.start();

process.on('SIGTERM', () => {
  sdk.shutdown().finally(() => process.exit(0));
});
```

#### Step 3: Run your App with Preloaded Telemetry
```bash
export OTEL_SERVICE_NAME="payment-service"
export OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:5000"
node --require ./tracer.js server.js
```

---

### 3. Python / FastAPI Service Integration

#### Step 1: Install OpenTelemetry Packages
```bash
pip install opentelemetry-distro \
            opentelemetry-exporter-otlp-proto-http \
            opentelemetry-instrumentation-fastapi
```

#### Step 2: Zero-Code Auto-Instrumentation Execution
```bash
export OTEL_SERVICE_NAME="inventory-service"
export OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:5000"
export OTEL_EXPORTER_OTLP_PROTOCOL="http/protobuf"

opentelemetry-instrument uvicorn main:app --host 0.0.0.0 --port 8000
```

#### Or Programmatic Configuration in `main.py`:
```python
from fastapi import FastAPI
from opentelemetry import trace, metrics
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
from opentelemetry.sdk.resources import Resource
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor

resource = Resource.create({"service.name": "inventory-service"})

provider = TracerProvider(resource=resource)
processor = BatchSpanProcessor(OTLPSpanExporter(endpoint="http://localhost:5000/v1/traces"))
provider.add_span_processor(processor)
trace.set_tracer_provider(provider)

app = FastAPI()
FastAPIInstrumentor.instrument_app(app)

@app.get("/items/{item_id}")
def get_item(item_id: int):
    return {"item_id": item_id, "in_stock": True}
```

---

### 4. OpenTelemetry Collector (Sidecar / Gateway) Integration

If you already run an OpenTelemetry Collector gateway or Kubernetes DaemonSet, route telemetry to KestrelScope by configuring an `otlphttp` exporter in `otel-collector-config.yaml`:

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318

processors:
  batch:
    timeout: 5s
    send_batch_size: 256

exporters:
  otlphttp/kestrelscope:
    endpoint: "http://kestrelscope:5000"

service:
  pipelines:
    traces:
      receivers: [otlp]
      processors: [batch]
      exporters: [otlphttp/kestrelscope]
    metrics:
      receivers: [otlp]
      processors: [batch]
      exporters: [otlphttp/kestrelscope]
    logs:
      receivers: [otlp]
      processors: [batch]
      exporters: [otlphttp/kestrelscope]
```

---

### 5. Raw HTTP / cURL Ingestion

You can emit telemetry directly from shell scripts, CI/CD pipelines, or legacy applications using standard JSON payloads:

#### Ingesting a Metric Sample (`POST /v1/metrics`)
```bash
curl -X POST http://localhost:5000/v1/metrics \
  -H "Content-Type: application/json" \
  -d '{
    "resourceMetrics": [{
      "resource": {
        "attributes": [{ "key": "service.name", "value": { "stringValue": "billing-service" } }]
      },
      "scopeMetrics": [{
        "metrics": [{
          "name": "invoices.processed.count",
          "gauge": {
            "dataPoints": [{
              "asDouble": 12.0,
              "timeUnixNano": "'$(date +%s%N)'"
            }]
          }
        }]
      }]
    }]
  }'
```

#### Ingesting a Distributed Trace Span (`POST /v1/traces`)
```bash
curl -X POST http://localhost:5000/v1/traces \
  -H "Content-Type: application/json" \
  -d '{
    "resourceSpans": [{
      "resource": {
        "attributes": [{ "key": "service.name", "value": { "stringValue": "billing-service" } }]
      },
      "scopeSpans": [{
        "spans": [{
          "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
          "spanId": "00f067aa0ba902b7",
          "name": "InvoiceProcessor.GeneratePdf",
          "startTimeUnixNano": "'$(($(date +%s%N) - 50000000))'",
          "endTimeUnixNano": "'$(date +%s%N)'",
          "status": { "code": 1 }
        }]
      }]
    }]
  }'
```

#### Ingesting a Structured Log with Trace Correlation (`POST /v1/logs`)
```bash
curl -X POST http://localhost:5000/v1/logs \
  -H "Content-Type: application/json" \
  -d '{
    "resourceLogs": [{
      "resource": {
        "attributes": [{ "key": "service.name", "value": { "stringValue": "billing-service" } }]
      },
      "scopeLogs": [{
        "logRecords": [{
          "timeUnixNano": "'$(date +%s%N)'",
          "severityText": "INFO",
          "severityNumber": 9,
          "body": { "stringValue": "Invoice #1094 created for customer CUST-44" },
          "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
          "spanId": "00f067aa0ba902b7",
          "attributes": [
            { "key": "customer.id", "value": { "stringValue": "CUST-44" } },
            { "key": "invoice.amount", "value": { "stringValue": "$199.00" } }
          ]
        }]
      }]
    }]
  }'
```

---

## 5. Using the Web Console

Open your browser to `http://localhost:5000/` to explore your service telemetry through the Microsoft Fluent 2 Dark Theme interface.

### Metrics Explorer
- **URL**: `http://localhost:5000/dashboard.html`
- **Features**:
  - **KPI Cards**: Active monitored service count, sample ingest rates, and triggered alerts.
  - **Filters**: Select any detected service and metric series from dropdown menus.
  - **Time Range**: Toggle between `15m`, `1h`, `6h`, and `24h` windows.
  - **Performance Graph**: Real-time line graph rendered via Chart.js with hover tooltips and sample point counts.

### Traces Explorer & Waterfall View
- **URL**: `http://localhost:5000/traces.html`
- **Features**:
  - **Left Panel (Trace DataGrid)**: Clean table displaying Trace ID, HTTP Method, Endpoint, Status (`200 OK`, `500 Error`), and Latency Duration.
  - **Right Panel (Waterfall Hierarchy)**: Click any trace in the list to reveal the nested execution tree of child spans. Microsecond execution bars indicate exact call durations, component boundaries, and failure points.
  - **Correlated Logs Link**: Click the `📜 View Correlated Logs` action button to open the log feed pre-filtered for the selected trace.

### Logs Explorer & Trace Correlation
- **URL**: `http://localhost:5000/logs.html`
- **Features**:
  - **Real-Time Stream**: Live view of structured application logs with severity badges (`ERROR`, `WARN`, `INFO`, `DEBUG`).
  - **Search & Filters**: Search message text, filter by service name, filter by severity, or isolate a specific `TraceId`.
  - **Trace Pills**: Click any `🔍 <TraceId>` pill badge in the table to jump directly to the execution waterfall in the Traces Explorer.
  - **Attributes Expansion**: Structured OTLP key-value attributes are formatted and embedded under each log entry.

### Alert Rules & Webhooks
- **URL**: `http://localhost:5000/alerts.html`
- **Features**:
  - **Rule Definition**: Click `+ New Alert Rule` to configure automatic threshold evaluations.
  - **Parameters**: Specify Metric Name (e.g., `http.server.request.duration`), Threshold Value (e.g., `500`), Evaluation Window (1m to 60m), and target Webhook URL.
  - **Evaluation Engine**: The background `AlertRuleWorker` evaluates average values across the specified window every 60 seconds.
  - **Webhook Payload**: When an alert breaches, KestrelScope dispatches a POST request to your webhook URL with incident details, and dispatches a resolution event when metrics normalize.

---

## 6. Database Management & Maintenance

KestrelScope stores all telemetry in an embedded SQLite database (`observability.db`) using **Write-Ahead Logging (WAL)** mode.

### Database File Structure
- `observability.db`: Main SQLite relational database.
- `observability.db-wal`: Write-Ahead Log storing concurrent writes before checkpointing.
- `observability.db-shm`: Shared-memory index used for lock-free reads.

### Backup Strategy
Because WAL mode allows lock-free concurrent reads while writes are active, you can back up the live database safely without stopping KestrelScope:

```bash
# Perform an online SQLite backup using the sqlite3 CLI
sqlite3 observability.db ".backup 'observability-backup-$(date +%Y%m%d).db'"
```

### Relational Schema Reference
- `MetricSamples`: `(Id, Timestamp, ServiceName, MetricName, Value)`
- `Spans`: `(Id, TraceId, SpanId, ParentSpanId, ServiceName, SpanName, StartNano, EndNano, DurationMs, StatusCode)`
- `Logs`: `(Id, Timestamp, TraceId, SpanId, ServiceName, SeverityText, SeverityNumber, Body, AttributesJson)`
- `AlertRules`: `(Id, Name, MetricName, Threshold, WindowMinutes, WebhookUrl, IsEnabled)`
- `AlertIncidents`: `(Id, RuleId, TriggeredAt, ResolvedAt, MetricValue, Status)`

---

## 7. Troubleshooting & Verification

### Issue: Telemetry is Not Appearing in the Dashboard
1. Verify KestrelScope is running and healthy:
   ```bash
   curl -i http://localhost:5000/health
   ```
2. Verify network connectivity from the client service:
   ```bash
   curl -i -X POST http://localhost:5000/v1/metrics -H "Content-Type: application/json" -d '{"resourceMetrics":[]}'
   ```
3. Check application logs for OTLP exporter errors (e.g., connection refused, wrong port).

### Issue: Port 5000 is Already in Use
Specify an alternative port at startup:
```bash
./KestrelScope --urls "http://0.0.0.0:5050"
```
Remember to update your service's `OTEL_EXPORTER_OTLP_ENDPOINT` to match:
```bash
export OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:5050"
```

### Running the End-to-End Automated Test Suite
KestrelScope includes a complete integration test suite exercising ingestion, tracing, log correlation, and alerting against a sample microservice:

```bash
# Run automated tests on host
dotnet test tests/KestrelScope.EndToEndTests

# Or run tests fully isolated in Docker CE
./scripts/run-e2e.sh
```

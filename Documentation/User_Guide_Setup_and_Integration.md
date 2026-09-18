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
   - [Application Flow Map & Service Topology](#application-flow-map--service-topology)
   - [Traces Explorer & Waterfall View](#traces-explorer--waterfall-view)
   - [Logs Explorer & Trace Correlation](#logs-explorer--trace-correlation)
   - [Alert Rules & Webhooks](#alert-rules--webhooks)
   - [Users Management & Role-Based Access Control](#users-management--role-based-access-control)
   - [Database Management Console](#database-management-console)
6. [Database Management & Admin REST API](#6-database-management--admin-rest-api)
   - [Core Architecture & Lifecycle Worker](#core-architecture--lifecycle-worker)
   - [Online Hot Backups & Gzip Compression](#online-hot-backups--gzip-compression)
   - [Disaster Recovery & Fail-Safe Restore Protocol](#disaster-recovery--fail-safe-restore-protocol)
   - [Chunked Telemetry Pruning & Dry-Run Simulation](#chunked-telemetry-pruning--dry-run-simulation)
   - [Engine Operations (Vacuum, Checkpoint, Integrity)](#engine-operations-vacuum-checkpoint-integrity)
   - [Administrative Compliance Audit Logging](#administrative-compliance-audit-logging)
   - [Admin REST API Reference](#admin-rest-api-reference)
   - [Programmatic cURL Administration Examples](#programmatic-curl-administration-examples)
7. [Troubleshooting & Verification](#7-troubleshooting--verification)

---

## 1. Platform Overview & Architecture

KestrelScope is an **air-gapped, sovereign, single-binary observability platform** engineered in .NET 9. It provides complete observability across the three core telemetry pillars without requiring external databases (no ClickHouse, PostgreSQL, or Redis), third-party SaaS agents, or external CDN dependencies.

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
│                           KestrelScope Platform                           │
│                                                                           │
│  ┌─────────────────────── OTLP Ingestion Engine ───────────────────────┐  │
│  │   POST /v1/metrics         POST /v1/traces         POST /v1/logs    │  │
│  └──────────┬──────────────────────┬──────────────────────┬────────────┘  │
│             │                      │                      │               │
│             ▼                      ▼                      ▼               │
│  ┌────────────────── Embedded SQLite Database (WAL Mode) ──────────────┐  │
│  │   MetricSamples           Spans & Events          Structured Logs   │  │
│  │   AlertRules              AlertIncidents          Users & Sessions  │  │
│  │   DatabaseSettings        AdminAuditLogs          Backups Metastore │  │
│  └─────────────────────────────────┬───────────────────────────────────┘  │
│                                    │                                      │
│  ┌─────────────────────── Background Services ─────────────────────────┐  │
│  │   • AlertRuleWorker (Automated metric evaluation & webhook dispatch)│  │
│  │   • DatabaseMaintenanceWorker (Nightly pruning & automated backups) │  │
│  └─────────────────────────────────┬───────────────────────────────────┘  │
│                                    │                                      │
│  ┌────────────────────── Fluent 2 Web Console ─────────────────────────┐  │
│  │   • Metrics Explorer               • Application Flow Map (Topology)│  │
│  │   • Traces Waterfall               • Structured Logs Explorer       │  │
│  │   • Alert Rules Manager            • User Management (Admin RBAC)   │  │
│  │   • Database Management Console (Storage, Backups, Restore, Audit)  │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────────────────────────────────────────────────┘
```

### Key Ingestion & Admin Endpoints
| Endpoint | Method | Role | Description |
| :--- | :--- | :--- | :--- |
| `/v1/metrics` | `POST` | Public | Ingests metric data points, gauges, and counters (OTLP/HTTP) |
| `/v1/traces` | `POST` | Public | Ingests distributed trace spans and parent-child hierarchies (OTLP/HTTP) |
| `/v1/logs` | `POST` | Public | Ingests structured log events with correlated Trace/Span IDs (OTLP/HTTP) |
| `/health` | `GET` | Public | Service liveness health check |
| `/api/topology` | `GET` | Standard | Inferred service dependency graph and inter-service telemetry |
| `/api/users` | `*` | Admin | Manage user accounts, credentials, and role permissions |
| `/api/admin/database/*` | `*` | Admin | Complete database lifecycle: retention, pruning, backup, restore, audit |

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
   Description=KestrelScope Observability Platform
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
- **Access**: Standard & Administrator
- **Features**:
  - **KPI Cards**: Active monitored service count, sample ingest rates, and triggered alerts.
  - **Filters**: Select any detected service and metric series from dropdown menus.
  - **Time Range**: Toggle between `15m`, `1h`, `6h`, and `24h` windows.
  - **Performance Graph**: Real-time line graph rendered via Chart.js with hover tooltips and sample point counts.

### Application Flow Map & Service Topology
- **URL**: `http://localhost:5000/flowmap.html`
- **Access**: Standard & Administrator
- **Features**:
  - **Inferred Service Dependency Graph**: Automatically detects service-to-service communication paths from distributed trace parent-child span boundaries.
  - **Live Inter-Service Telemetry**: Directed connector edges display real-time call volume (RPS), error rates (%), and average roundtrip latency (ms).
  - **Interactive Draggable Nodes**: Arrange service topology nodes on an infinite canvas with positions automatically cached in session storage.
  - **Service Inspector Drawer**: Click any node to slide open a dedicated telemetry panel showing inbound/outbound dependencies, active throughput, and health status badges.

### Traces Explorer & Waterfall View
- **URL**: `http://localhost:5000/traces.html`
- **Access**: Standard & Administrator
- **Features**:
  - **Left Panel (Trace DataGrid)**: Clean table displaying Trace ID, HTTP Method, Endpoint, Status (`200 OK`, `500 Error`), and Latency Duration.
  - **Right Panel (Waterfall Hierarchy)**: Click any trace in the list to reveal the nested execution tree of child spans. Microsecond execution bars indicate exact call durations, component boundaries, and failure points.
  - **Correlated Logs Link**: Click the `📜 View Correlated Logs` action button to open the log feed pre-filtered for the selected trace.

### Logs Explorer & Trace Correlation
- **URL**: `http://localhost:5000/logs.html`
- **Access**: Standard & Administrator
- **Features**:
  - **Real-Time Stream**: Live view of structured application logs with severity badges (`ERROR`, `WARN`, `INFO`, `DEBUG`).
  - **Search & Filters**: Search message text, filter by service name, filter by severity, or isolate a specific `TraceId`.
  - **Trace Pills**: Click any `🔍 <TraceId>` pill badge in the table to jump directly to the execution waterfall in the Traces Explorer.
  - **Attributes Expansion**: Structured OTLP key-value attributes are formatted and embedded under each log entry.

### Alert Rules & Webhooks
- **URL**: `http://localhost:5000/alerts.html`
- **Access**: Standard (Read-Only) / Administrator (Create, Edit, Delete)
- **Features**:
  - **Rule Definition**: Click `+ New Alert Rule` to configure automatic threshold evaluations.
  - **Parameters**: Specify Metric Name (e.g., `http.server.request.duration`), Threshold Value (e.g., `500`), Evaluation Window (1m to 60m), and target Webhook URL.
  - **Evaluation Engine**: The background `AlertRuleWorker` evaluates average values across the specified window every 60 seconds.
  - **Webhook Payload**: When an alert breaches, KestrelScope dispatches a POST request to your webhook URL with incident details, and dispatches a resolution event when metrics normalize.

### Users Management & Role-Based Access Control
- **URL**: `http://localhost:5000/users.html`
- **Access**: Administrator Only (Enforced via RBAC)
- **Features**:
  - **Role Separation**:
    - **Administrator (`admin`)**: Full platform control, including database operations, user management, and alert rule modifications.
    - **Standard (`standard`)**: Telemetry exploration, flow map monitoring, and read-only alert visibility.
  - **User Lifecycle**: Create accounts, assign roles, and update user credentials.
  - **Security Safeguards**: The active logged-in administrator is prevented from deleting their own account to prevent accidental lockout.

### Database Management Console
- **URL**: `http://localhost:5000/database.html`
- **Access**: Administrator Only (Guarded via route and API RBAC)
- **Features (5 Dedicated Management Panes)**:
  1. **Overview & Storage**:
     - Real-time disk utilization: Active SQLite DB size, WAL journal size, SHM index size, and reclaimable freelist pages.
     - Live SQLite schema table breakdown showing row counts, byte sizes, and telemetry timestamp spans (`oldest -> newest`).
     - Engine operations: One-click buttons to execute `PRAGMA integrity_check`, sync and truncate the WAL journal (`PRAGMA wal_checkpoint(TRUNCATE)`), or run a full database `VACUUM`.
     - Interactive storage warning banner when active database size exceeds the configured warning threshold.
  2. **Retention & Pruning**:
     - Global retention policy settings: Set expiration timeframes (days) for MetricSamples, Spans, Logs, and Alerts.
     - Automated background pruning scheduler: Configure daily execution hour (UTC).
     - On-demand chunked pruner: Target specific telemetry tables or purge all expired records with dry-run estimation simulation before permanent execution.
  3. **Backups Catalog**:
     - Online hot backup creation with custom labels and optional Gzip compression (`.db.gz`).
     - Interactive snapshot catalog displaying filename, backup type (`Manual`, `Scheduled`, `Safety Rollback`), formatted size, creation timestamp, and SHA256 integrity checksum.
     - Direct browser downloads, one-click restore triggers, and snapshot deletion.
  4. **Disaster Recovery**:
     - High-safeguard restoration engine requiring strict confirmation typing (`CONFIRM_RESTORE`).
     - **Automated Safety Rollback Snapshot**: KestrelScope automatically captures an emergency snapshot of the current active database before overwriting data, allowing restorations to be undone at any time.
  5. **Audit Trail**:
     - Compliance activity stream logging administrative actions (`PRUNE`, `BACKUP_CREATE`, `BACKUP_DELETE`, `RESTORE`, `RETENTION_UPDATE`, `VACUUM`, `CHECKPOINT`).
     - Records admin username, timestamp (UTC), client IP address, target tables/snapshots, and execution details for SOC2 / audit compliance.

---

## 6. Database Management & Admin REST API

KestrelScope features an enterprise-grade database lifecycle engine engineered for 100% sovereign operation without external database administrators.

### Core Architecture & Lifecycle Worker
- **Dual-Worker Concurrency**: In addition to `AlertRuleWorker`, KestrelScope runs `DatabaseMaintenanceWorker` as an ASP.NET Core `BackgroundService`.
- **Automated Nightly Maintenance**: Evaluates active retention policies once per hour. When the configured UTC hour matches, it automatically executes chunked retention pruning, triggers a daily hot backup, and rotates historical backups according to `BackupRetentionCount`.
- **Lock-Free Ingestion**: Telemetry ingestion continues uninhibited during maintenance operations thanks to SQLite Write-Ahead Logging (WAL) mode and chunked transaction isolation.

### Online Hot Backups & Gzip Compression
Rather than relying on risky file-copy operations while SQLite is writing, KestrelScope invokes the native **SQLite Online Backup API** (`SqliteConnection.BackupDatabase()`):
- Creates crash-consistent, byte-exact snapshots while writes are active in the WAL journal.
- **Gzip Streaming Compression**: Automatically streams backup bytes through `GZipStream`, generating `.db.gz` archives that reduce disk footprints by 70–85%.
- **SHA256 Checksums**: Every snapshot calculates a SHA256 checksum during generation, displayed in the catalog and stored in the metadata.

### Disaster Recovery & Fail-Safe Restore Protocol
To prevent catastrophic accidental data loss during restorations:
1. The operator selects a snapshot and must provide the verification token `CONFIRM_RESTORE`.
2. KestrelScope immediately runs an online hot backup of the *current* active database, creating a rollback snapshot named `pre-restore-safety-<timestamp>.db`.
3. If the selected snapshot is compressed (`.db.gz`), it is uncompressed into a temporary database file.
4. Structural integrity is validated on the target file via `PRAGMA quick_check;`.
5. The live database connection is safely synchronized using SQLite page backup restoration.
6. Schema initialization verifies table consistency and migrations.

### Chunked Telemetry Pruning & Dry-Run Simulation
To avoid holding long table locks on multi-gigabyte SQLite databases, deletions are partitioned into configurable batches (**5,000 rows per batch**):
- Pruning runs in iterative loops: `DELETE FROM Table WHERE Id IN (SELECT Id FROM Table WHERE Timestamp < @Cutoff LIMIT 5000);`
- **Dry-Run Mode**: Operators can run a simulation (`dryRun = true`) that queries candidate counts without deleting data, reporting estimated reclaimable bytes and execution times.

### Engine Operations (Vacuum, Checkpoint, Integrity)
- **`VACUUM`**: Rebuilds the database file to reclaim unused space from deleted records and defragment data pages.
- **`PRAGMA wal_checkpoint(TRUNCATE)`**: Flushes uncommitted pages from the `.db-wal` file back into the primary `.db` file and truncates the WAL file to zero bytes.
- **`PRAGMA integrity_check` & `foreign_key_check`**: Scans the database B-tree structure and relational foreign keys for physical corruption or inconsistency.

### Administrative Compliance Audit Logging
Every administrative database mutation is captured in the relational `AdminAuditLogs` table:
- **Recorded Fields**: `Id`, `Timestamp`, `Username`, `Action`, `Target`, `DetailsJson`, `IpAddress`.
- Provides an immutable compliance trail for SOC2, HIPAA, and ISO27001 audit standards.

### Admin REST API Reference

All database administration endpoints are rooted at `/api/admin/database` and strictly require authentication with `Role == 'Admin'`. Unauthorized or standard user requests are rejected with `401 Unauthorized` or `403 Forbidden`.

| Method | Endpoint | Description | Request Body | Response Model |
| :--- | :--- | :--- | :--- | :--- |
| `GET` | `/api/admin/database/storage` | Get database disk usage, WAL, freelist, and table breakdown | None | `DatabaseStorageStatsDto` |
| `GET` | `/api/admin/database/health` | Run integrity check and foreign key check | None | `DatabaseHealthDto` |
| `GET` | `/api/admin/database/retention` | Get active retention policies and schedules | None | `RetentionPolicyDto` |
| `PUT` | `/api/admin/database/retention` | Update retention policies, scheduler hours, warning threshold | `UpdateRetentionPolicyRequest` | `RetentionPolicyDto` |
| `POST` | `/api/admin/database/prune` | Execute on-demand or dry-run chunked pruning | `PruneRequest` | `PruneResultDto` |
| `POST` | `/api/admin/database/vacuum` | Execute full VACUUM compaction | None | `{ message: string }` |
| `POST` | `/api/admin/database/checkpoint` | Flush and truncate WAL journal | None | `{ message: string }` |
| `GET` | `/api/admin/database/backups` | List all available backup snapshots in catalog | None | `IEnumerable<BackupItemDto>` |
| `POST` | `/api/admin/database/backups` | Create a live online hot backup snapshot | `CreateBackupRequest` | `BackupItemDto` |
| `GET` | `/api/admin/database/backups/{fileName}/download` | Stream download of backup snapshot file | None | Binary (`application/octet-stream`) |
| `DELETE` | `/api/admin/database/backups/{fileName}` | Permanently delete a backup snapshot | None | `{ message: string }` |
| `POST` | `/api/admin/database/restore` | Restore database snapshot with automated safety rollback | `RestoreRequest` | `RestoreResultDto` |
| `GET` | `/api/admin/database/audit` | Query administrative compliance audit trail | Query `limit=100` | `IEnumerable<AdminAuditLogDto>` |

### Programmatic cURL Administration Examples

Ensure you authenticate first via `POST /api/auth/login` to obtain an authenticated session cookie:

```bash
# 1. Log in as an Administrator
curl -c cookies.txt -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}'

# 2. Inspect Live Database Storage Breakdown
curl -b cookies.txt http://localhost:5000/api/admin/database/storage

# 3. Trigger a Dry-Run Pruning Simulation (Override to 7 days retention)
curl -b cookies.txt -X POST http://localhost:5000/api/admin/database/prune \
  -H "Content-Type: application/json" \
  -d '{
    "target": "all",
    "customRetentionDays": 7,
    "dryRun": true
  }'

# 4. Create an On-Demand Compressed Backup Snapshot
curl -b cookies.txt -X POST http://localhost:5000/api/admin/database/backups \
  -H "Content-Type: application/json" \
  -d '{"label":"pre-upgrade-milestone","compress":true}'

# 5. Download a Backup Snapshot Archive
curl -b cookies.txt -OJ http://localhost:5000/api/admin/database/backups/kestrelscope-backup-20260918-171915.db.gz

# 6. Flush and Truncate WAL Journal
curl -b cookies.txt -X POST http://localhost:5000/api/admin/database/checkpoint

# 7. Execute Disaster Recovery Restore (Requires confirmation token)
curl -b cookies.txt -X POST http://localhost:5000/api/admin/database/restore \
  -H "Content-Type: application/json" \
  -d '{
    "backupFileName": "kestrelscope-backup-20260918-171915.db.gz",
    "confirmationToken": "CONFIRM_RESTORE"
  }'
```

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

### Issue: Storage Warning Threshold Exceeded Banner
If the Web Console displays a high-water mark storage warning banner:
1. Navigate to **Database Management** (`/database.html`).
2. Run **Integrity Check** to confirm database health.
3. In **Retention & Pruning**, execute an **On-Demand Telemetry Pruning** (or perform a Dry Run first to preview reclaimable space).
4. Run **Flush WAL Checkpoint** and **Vacuum Database** to reclaim deleted space back to the operating system.
5. If necessary, increase the `Storage Warning Threshold (MB)` in the Retention tab.

### Issue: Restoring After Accidental Data Loss
If bad telemetry was ingested or data corruption occurred:
1. Navigate to **Disaster Recovery** in the Database Management console.
2. Select a verified historical snapshot from the dropdown.
3. Type `CONFIRM_RESTORE` and click **Authorize & Restore Database Snapshot**.
4. KestrelScope will automatically capture a `pre-restore-safety-*.db` snapshot of the current state before applying the restored snapshot.
5. If the restore needs to be reverted, select the `Safety Rollback` snapshot from the Backups Catalog.

### Running the End-to-End Automated Test Suite
KestrelScope includes a complete integration test suite exercising ingestion, tracing, log correlation, alerting, RBAC enforcement, pruning, online hot backups, and disaster recovery restore:

```bash
# Run automated tests on host
dotnet test tests/KestrelScope.EndToEndTests

# Or run tests fully isolated in Docker CE
./scripts/run-e2e.sh
```


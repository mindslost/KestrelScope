# KestrelScope — Self-Hosted .NET Observability Platform

[![CI](https://github.com/mindslost/KestrelScope/actions/workflows/ci.yml/badge.svg)](https://github.com/mindslost/KestrelScope/actions/workflows/ci.yml)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512bd4.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![OpenTelemetry](https://img.shields.io/badge/OpenTelemetry-OTLP%201.0-4a154b.svg)](https://opentelemetry.io/)
[![Design System](https://img.shields.io/badge/Design%20System-Microsoft%20Fluent%202-0f6cbd.svg)](https://fluent2.microsoft.design/)
[![License: Apache 2.0 w/ Commons Clause](https://img.shields.io/badge/License-Apache_2.0_w%2F_Commons_Clause-blue.svg)](LICENSE.md)

KestrelScope is a **100% sovereign, self-hosted, single-binary observability platform** engineered in .NET 9 and SQLite. It provides native OpenTelemetry (OTLP) ingestion for **Metrics**, **Distributed Traces**, and **Structured Logs** with bidirectional trace correlation, an embedded relational metastore, a background alert evaluation engine, and a modern web dashboard built on the **Microsoft Fluent 2 Design System**.

---

## 📖 User Documentation
For detailed setup guides, Docker deployment, and code integration examples across **.NET**, **Node.js**, **Python**, **OTel Collector**, and **cURL**, see:
👉 [**User Guide: Setup & Service Integration (Documentation/User_Guide_Setup_and_Integration.md)**](Documentation/User_Guide_Setup_and_Integration.md)

---

## Architectural Mandates
1. **Single-Binary Architecture**: Runs as a single process containing web API endpoints, static UI assets (`wwwroot`), database engine, and background workers. No external database dependencies (no ClickHouse, PostgreSQL, or Redis) and zero SaaS vendor locks.
2. **Air-Gapped Operation**: 100% sovereign. All static frontend assets (HTML, Fluent CSS tokens, JS, Chart.js, embedded SVG Fluent System Icons) are served locally from `wwwroot/`. Zero external CDN calls or external font downloads required.
3. **High-Concurrency Persistence**: Uses SQLite in Write-Ahead Logging (WAL) mode (`PRAGMA journal_mode=WAL;`) with Dapper for high-throughput, concurrent, lock-free read/write ingestion.
4. **Three Pillars of Observability**: Ingests standard OpenTelemetry payloads over HTTP:
   - `POST /v1/metrics` — Gauges, counters, and performance series
   - `POST /v1/traces` — Distributed traces and hierarchical span waterfall
   - `POST /v1/logs` — Structured logs with bidirectional trace correlation
5. **Fluent 2 User Experience**: Modern Microsoft Fluent 2 dark theme interface featuring Acrylic/Mica surfaces, depth shadows, CommandBar navigation, compact DataGrids, and status pill badges.

---

## Quickstart & Build

### 1. Run on Host (.NET 9)
```bash
# Build the solution
dotnet build

# Run KestrelScope on port 5000
dotnet run --urls "http://0.0.0.0:5000"
```

### 2. Run via Docker Compose
```bash
# Start KestrelScope in background
docker compose up -d kestrelscope
```

### 3. Verify Health & Open Dashboard
- **Health Check**: `curl -s http://localhost:5000/health`
- **Web Console**: Open [http://localhost:5000](http://localhost:5000) in your browser
  - **Default Username**: `admin`
  - **Default Password**: `admin123`

---

## Quick Integration (Send Telemetry to KestrelScope)

Set standard OpenTelemetry environment variables in any microservice:

```bash
export OTEL_SERVICE_NAME="my-service"
export OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:5000"
export OTEL_EXPORTER_OTLP_PROTOCOL="http/protobuf" # or http/json
```

### Ingesting via cURL
```bash
# Ingest Log Record with Trace Correlation
curl -X POST http://localhost:5000/v1/logs \
  -H "Content-Type: application/json" \
  -d '{
    "resourceLogs": [{
      "resource": { "attributes": [{ "key": "service.name", "value": { "stringValue": "payment-api" } }] },
      "scopeLogs": [{
        "logRecords": [{
          "timeUnixNano": "'$(date +%s%N)'",
          "severityText": "INFO",
          "severityNumber": 9,
          "body": { "stringValue": "Payment processed successfully" },
          "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
          "spanId": "00f067aa0ba902b7"
        }]
      }]
    }]
  }'
```

---

## Web Console Features

| View | Path | Description | Access |
| :--- | :--- | :--- | :--- |
| **Landing Portal** | `/` | Fluent 2 Acrylic launcher with live operational health badge | All Users |
| **Metrics Explorer** | `/dashboard.html` | Live KPI cards, service & metric filters, segmented time windows, and Chart.js telemetry graphs | Standard / Admin |
| **Application Flow Map** | `/flowmap.html` | Multi-service topology graph with live inter-service RPS, error rates, latencies, draggable node caching, and inspector drawer | Standard / Admin |
| **Traces Explorer** | `/traces.html` | Master-detail split view with compact DataGrid, execution waterfalls, and correlated log links | Standard / Admin |
| **Logs Explorer** | `/logs.html` | Structured log feed, severity filters, live text search, and clickable trace correlation pills | Standard / Admin |
| **Alert Rules** | `/alerts.html` | Threshold alert rule manager with automated background worker evaluation and webhook dispatchers | Standard (Read) / Admin (Edit) |
| **Users Management** | `/users.html` | User accounts, credentials, and Role-Based Access Control (Admin vs Standard) | Admin Only |
| **Database Management** | `/database.html` | Storage diagnostics, retention policies, chunked pruning, online hot backups (.db.gz), disaster recovery restore, and SOC2 audit trail | Admin Only |

---

## Automated Tests

Run the complete integration test suite exercising end-to-end metrics, traces, structured logs, and alert triggers:

```bash
# Run tests on host
dotnet test tests/KestrelScope.EndToEndTests

# Run tests in Docker CE container
./scripts/run-e2e.sh
```

---

## Repository Directory Structure
```
├── Controllers/
│   ├── OtlpIngestionController.cs      # Ingests standard OTLP metrics, traces, and logs
│   ├── ApiControllers.cs               # Core REST APIs (/api/auth, /api/metrics, /api/logs, /api/alerts, /api/topology)
│   └── DatabaseAdminController.cs      # Admin database management endpoints (/api/admin/database/*)
├── Models/
│   ├── TelemetryModels.cs              # Core telemetry and alert DTOs
│   └── DatabaseManagementModels.cs     # Storage stats, backup items, prune/restore requests
├── Services/
│   ├── AlertRulerWorker.cs             # Background worker evaluating metric thresholds & dispatching webhooks
│   ├── DatabaseMaintenanceWorker.cs    # Background worker running scheduled pruning & automated backups
│   ├── IDatabaseManagementService.cs   # Database engine & lifecycle service contract
│   └── DatabaseManagementService.cs    # SQLite online hot backup, gzip compression, restore & pruning engine
├── Documentation/
│   └── User_Guide_Setup_and_Integration.md # Complete user setup and integration guide
├── tests/
│   ├── SampleOrderService/             # Reference microservice emitting OTel telemetry
│   └── KestrelScope.EndToEndTests/     # xUnit automated end-to-end test suite
├── website/                            # Static product & documentation website (GitHub Pages)
├── wwwroot/                            # Sovereign, air-gapped Fluent 2 web UI assets
│   ├── index.html                      # Landing portal
│   ├── login.html                      # Session authentication
│   ├── dashboard.html                  # Metrics explorer
│   ├── flowmap.html                    # Application flow map & topology graph
│   ├── traces.html                     # Traces explorer & execution waterfall
│   ├── logs.html                       # Structured logs explorer
│   ├── alerts.html                     # Alert rules manager
│   ├── users.html                      # User management & RBAC console
│   ├── database.html                   # Database management & disaster recovery console
│   ├── css/main.css                    # Microsoft Fluent 2 Dark Theme stylesheet
│   └── js/app.js                       # Client application controller & UI modules
├── CONTRIBUTING.md                     # Community contribution guidelines & license binding
├── LICENSE.md                          # Apache 2.0 with Commons Clause Condition v1.0
├── DbInitializer.cs                    # SQLite schema initializer, WAL configuration & settings seed
├── Dockerfile                          # Production container definition
├── docker-compose.yml                  # Local container orchestration
└── Program.cs                          # Application entry point & service dependency injection
```

## Contributing

Contributions, bug reports, and enhancements are welcome! All submissions are bound to the project's Apache 2.0 with Commons Clause license terms. Please see [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines.

## License

This project is licensed under the **Apache License, Version 2.0 with the "Commons Clause" License Condition v1.0** — see [LICENSE.md](LICENSE.md) for full terms.

- **Free for Individuals & Organizations**: You may freely use, inspect, adapt, run, and modify KestrelScope for personal, educational, and internal business operations.
- **Commercial Restrictions**: Under the Commons Clause Condition v1.0, selling, sublicensing, or offering KestrelScope as a managed commercial service (SaaS) where the value derives substantially from the software's functionality is strictly prohibited without prior written authorization from the Licensor.


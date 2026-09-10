# KestrelScope — Self-Hosted .NET Observability Monolith

## Project Overview
KestrelScope is a 100% sovereign, self-hosted, single-binary observability service built with .NET 8/9 and SQLite. It provides native OpenTelemetry (OTLP) metrics and trace ingestion, an embedded relational metastore, a background alert evaluation engine, and a web dashboard for monitoring microservice health.

## Architectural Mandates
1. **Single-Binary Monolith**: Runs as a single process containing web API endpoints, static UI assets (`wwwroot`), database engine, and background workers. No external container dependencies (no ClickHouse, PostgreSQL, or Redis) and no SaaS vendors.
2. **Air-Gapped Operation**: All static frontend assets (HTML, CSS, JS, Chart.js) are hosted locally within `wwwroot/`. No external CDN calls or internet access required.
3. **High-Concurrency Persistence**: Uses SQLite in Write-Ahead Logging (WAL) mode (`PRAGMA journal_mode=WAL;`) for concurrent read/write throughput without database lock contention.
4. **Standard OTLP Receivers**: Exposes standard OpenTelemetry ingestion endpoints at `/v1/metrics` and `/v1/traces`.

## Repository Directory Structure
```
├── Program.cs                      # Application entry point & service bootstrap
├── DbInitializer.cs                # SQLite schema initializer & WAL configuration
├── Controllers/
│   ├── OtlpIngestionController.cs  # Ingests OTLP metric and trace JSON payloads
│   └── ApiControllers.cs           # Web UI REST APIs (/api/auth, /api/metrics, /api/alerts)
├── Services/
│   └── AlertRulerWorker.cs         # BackgroundService evaluating metric alerts every 60s
├── Tools/
│   └── SyntheticTelemetryGenerator.cs # Test utility emitting sample OTLP data
└── wwwroot/                        # Embedded web UI static assets
    ├── index.html                  # Public landing / platform status
    ├── login.html                  # Auth page
    ├── dashboard.html              # Metrics explorer with Chart.js
    ├── traces.html                 # Distributed trace viewer
    ├── alerts.html                 # Alert rule assignment UI
    ├── css/main.css                # Dark-mode dashboard stylesheet
    └── js/
        ├── chart.min.js            # Locally hosted Chart.js library
        └── app.js                  # API client & UI event handlers
```

## Quickstart & Build
```bash
# Build the project
dotnet build

# Run KestrelScope
dotnet run
```
The platform listens on `http://0.0.0.0:5000`.

## Implementation Status
- [x] **Phase 1: Solution Setup & Database Initialization**
  - [x] Create ASP.NET Core Web API project (.NET 9).
  - [x] Add NuGet packages: Microsoft.Data.Sqlite, Dapper.
  - [x] Implement `DbInitializer.cs` with `PRAGMA journal_mode=WAL;` and schema creation script.
  - [x] Verify database initialization creates `observability.db` on launch.
- [x] **Phase 2: OTLP Ingestion Controller**
  - [x] Implement `OtlpIngestionController.cs` with routes `/v1/metrics` and `/v1/traces`.
  - [x] Add JSON parsing logic for OTLP `resourceMetrics`, `resourceSpans`, and `service.name` attributes.
  - [x] Implement SQLite transaction batching for fast sample insertion.
  - [x] Implement `SyntheticTelemetryGenerator.cs` and verify ingestion end-to-end.
- [x] **Phase 3: Background Alert Engine**
  - [x] Create `AlertRulerWorker.cs` extending `BackgroundService`.
  - [x] Configure 60-second evaluation loop inside `ExecuteAsync`.
  - [x] Implement SQL aggregate threshold calculation query over `MetricSamples`.
  - [x] Implement alert state management to suppress notification fatigue.
  - [x] Implement resilient `HttpClient` webhook POST dispatching with 5-second timeouts on breaches and resolutions.
- [x] **Phase 4: Authentication & Static Web Pipeline**
  - [x] Enable static file middleware (`app.UseStaticFiles()`) mapping to `wwwroot/`.
  - [x] Implement Cookie Authentication middleware (`CookieAuth`, `ObsSession`).
  - [x] Build `/api/auth/login`, `/api/auth/logout`, and `/api/auth/me` API endpoints with PBKDF2 hashing.
  - [x] Build `/api/metrics/*` (services, names, series, stats), `/api/alerts/*` (CRUD, toggle), and `/api/traces` query REST endpoints.
- [x] **Phase 5: Web UI Development & Integration Testing**
  - [x] Embed local `chart.min.js` into `wwwroot/js/chart.min.js` (100% air-gapped).
  - [x] Implement dark-mode CSS in `wwwroot/css/main.css` matching provided design mockups.
  - [x] Build `index.html`, `login.html`, `dashboard.html`, `traces.html`, and `alerts.html`.
  - [x] Implement `app.js` managing session authentication, metrics live charts, hierarchical trace waterfall timelines, and alert rules management.
  - [x] Execute end-to-end integration tests verifying telemetry ingestion, alert evaluation, and UI asset serving.

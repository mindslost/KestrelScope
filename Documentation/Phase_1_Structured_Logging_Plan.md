# Implementation Plan: Phase 1 — Structured Logging (`/v1/logs`) & Trace Correlation

Deliver the missing 3rd pillar of observability: OpenTelemetry-compliant structured logging (`/v1/logs`), bidirectional trace-to-log correlation, high-performance SQLite WAL storage, REST APIs, an air-gapped dark-mode Logs Explorer UI, and automated test verification.

## User Review Required

> [!NOTE]
> All changes strictly preserve KestrelScope's sovereign single-binary architecture with zero external SaaS or CDN dependencies.

## Proposed Changes

### Database Layer
#### [MODIFY] [DbInitializer.cs](file:///home/jason/Projects/KestrelScope/DbInitializer.cs)
- Add `Logs` table schema in WAL mode:
  - `Id`, `Timestamp`, `TraceId`, `SpanId`, `ServiceName`, `SeverityText`, `SeverityNumber`, `Body`, `AttributesJson`.
  - Add indexes: `idx_logs_lookup` on `(ServiceName, Timestamp)` and `idx_logs_trace` on `TraceId`.
- Seed initial baseline log entries for demonstration.

---

### Ingestion Layer
#### [MODIFY] [Controllers/OtlpIngestionController.cs](file:///home/jason/Projects/KestrelScope/Controllers/OtlpIngestionController.cs)
- Add `POST /v1/logs` endpoint compliant with OpenTelemetry JSON log format (`resourceLogs`, `scopeLogs`, `logRecords`).
- Batch insert into SQLite within a single transaction.
- Extract `service.name`, `timeUnixNano`, `severityText`, `severityNumber`, `body`, `traceId`, `spanId`, and attributes.

---

### REST API Layer
#### [MODIFY] [Controllers/ApiControllers.cs](file:///home/jason/Projects/KestrelScope/Controllers/ApiControllers.cs)
- Add `LogsController` (`/api/logs`):
  - `GET /api/logs`: Query logs with filters: `service`, `severity`, `traceId`, `query` (text search), `minutes`, `limit`.
  - `GET /api/logs/services`: Distinct service names for log filtering.

---

### Frontend UI Layer
#### [NEW] [wwwroot/logs.html](file:///home/jason/Projects/KestrelScope/wwwroot/logs.html)
- Sovereign, dark-mode Logs Explorer matching `dashboard.html` and `traces.html`.
- Real-time controls: Service dropdown, Severity filter, Free-text search, Time window, Trace ID filter.
- Log feed displaying severity badges, monospace timestamps, messages, attributes, and clickable `[Trace: <id>]` badges navigating directly to the trace waterfall.
#### [MODIFY] [wwwroot/index.html](file:///home/jason/Projects/KestrelScope/wwwroot/index.html)
- Add Logs navigation link and landing card.
#### [MODIFY] [wwwroot/dashboard.html](file:///home/jason/Projects/KestrelScope/wwwroot/dashboard.html), [wwwroot/traces.html](file:///home/jason/Projects/KestrelScope/wwwroot/traces.html), [wwwroot/alerts.html](file:///home/jason/Projects/KestrelScope/wwwroot/alerts.html)
- Add "Logs" navbar link.
- In `traces.html`, add a direct "View Correlated Logs" link on trace waterfalls.
#### [MODIFY] [wwwroot/js/app.js](file:///home/jason/Projects/KestrelScope/wwwroot/js/app.js)
- Add logs query client, filtering, rendering, and trace correlation helper functions.

---

### Telemetry Test Service
#### [MODIFY] [tests/SampleOrderService/Program.cs](file:///home/jason/Projects/KestrelScope/tests/SampleOrderService/Program.cs)
- Add log queue and OTLP `/v1/logs` JSON payload builder in `TelemetryDispatcher`.
- Emit correlated `INFO`, `WARN`, and `ERROR` logs during order operations and payment failure.

---

### Automated Verification
#### [MODIFY] [tests/KestrelScope.EndToEndTests/EndToEndPipelineTests.cs](file:///home/jason/Projects/KestrelScope/tests/KestrelScope.EndToEndTests/EndToEndPipelineTests.cs)
- Add `Test4_Logs_IngestionAndTraceCorrelation`:
  - Asserts `/api/logs` contains logs emitted by `order-service`.
  - Asserts trace-to-log correlation: querying `/api/logs?traceId={failTraceId}` returns exact ERROR log.
  - Asserts text search and severity filters.

---

## Verification Plan

### Automated Tests
- Run `dotnet test tests/KestrelScope.EndToEndTests` (all 4 tests must pass).
- Run `scripts/run-e2e.sh` with Docker CE to verify containerized execution.

### Manual Verification
- Access `http://localhost:5000/logs.html` to test live searching, severity filtering, and click-to-trace correlation.


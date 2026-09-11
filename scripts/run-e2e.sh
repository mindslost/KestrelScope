#!/usr/bin/env bash
set -e

# Configure Docker CE command
if command -v docker &> /dev/null && docker compose version &> /dev/null; then
    COMPOSE_CMD="docker compose"
elif command -v docker-compose &> /dev/null; then
    COMPOSE_CMD="docker-compose"
else
    echo "Error: Docker CE was not found or Docker Compose plugin is missing."
    echo "Please ensure Docker CE is installed and running."
    exit 1
fi

# Verify Docker daemon is responsive
if ! docker info &> /dev/null; then
    echo "Error: Docker daemon is not responding. Please check 'systemctl status docker'."
    exit 1
fi

echo "==> Using Docker CE Compose: ${COMPOSE_CMD}"

# Cleanup on exit trap (preserves database file on host)
cleanup() {
    echo "==> Stopping test containers..."
    ${COMPOSE_CMD} --profile test down 2>/dev/null || true
}
trap cleanup EXIT

# Pre-flight: ensure old test containers are stopped
${COMPOSE_CMD} --profile test down 2>/dev/null || true

echo "==> Building and starting KestrelScope and SampleOrderService with Docker CE..."
${COMPOSE_CMD} up -d --build kestrelscope sample-service

echo "==> Waiting for services to become healthy..."
MAX_WAIT=30
WAITED=0
until [ $WAITED -ge $MAX_WAIT ]; do
    K_HEALTH=$(curl -s http://localhost:5000/health || true)
    S_HEALTH=$(curl -s http://localhost:8080/health || true)
    if [[ "$K_HEALTH" == *"Healthy"* ]] && [[ "$S_HEALTH" == *"Healthy"* ]]; then
        echo "==> Services are healthy!"
        break
    fi
    sleep 2
    WAITED=$((WAITED + 2))
done

if [ $WAITED -ge $MAX_WAIT ]; then
    echo "==> Timed out waiting for services to become healthy."
    ${COMPOSE_CMD} logs
    exit 1
fi

echo "==> Running End-to-End Test Suite against live Docker CE containers..."
dotnet test tests/KestrelScope.EndToEndTests/KestrelScope.EndToEndTests.csproj --verbosity normal

echo "==> All End-to-End Tests Passed Successfully!"
echo "==> Telemetry data successfully saved to: $(pwd)/observability.db"

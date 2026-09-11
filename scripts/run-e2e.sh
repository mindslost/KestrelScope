#!/usr/bin/env bash
set -e

# Detect container runtime and socket
if command -v docker &> /dev/null && docker info &> /dev/null; then
    COMPOSE_CMD="docker compose"
elif [ -S "/run/user/$(id -u)/podman/podman.sock" ]; then
    export DOCKER_HOST="unix:///run/user/$(id -u)/podman/podman.sock"
    COMPOSE_CMD="docker compose"
elif command -v podman &> /dev/null; then
    systemctl --user start podman.socket 2>/dev/null || true
    if [ -S "/run/user/$(id -u)/podman/podman.sock" ]; then
        export DOCKER_HOST="unix:///run/user/$(id -u)/podman/podman.sock"
        COMPOSE_CMD="docker compose"
    else
        COMPOSE_CMD="podman compose"
    fi
else
    echo "Error: Neither Docker nor Podman could be connected to."
    exit 1
fi

echo "==> Using compose command: ${COMPOSE_CMD}"

# Cleanup on exit trap (perserves database file on host)
cleanup() {
    echo "==> Stopping test containers..."
    ${COMPOSE_CMD} --profile test down 2>/dev/null || true
}
trap cleanup EXIT

echo "==> Building and starting KestrelScope and SampleOrderService..."
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

echo "==> Running End-to-End Test Suite against live containers..."
dotnet test tests/KestrelScope.EndToEndTests/KestrelScope.EndToEndTests.csproj --verbosity normal

echo "==> All End-to-End Tests Passed Successfully!"
echo "==> Telemetry data successfully saved to: $(pwd)/observability.db"

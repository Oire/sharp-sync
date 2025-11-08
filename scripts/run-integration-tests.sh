#!/bin/bash

# Script to run SharpSync integration tests with Docker-based SFTP server
# Usage: ./scripts/run-integration-tests.sh [test-filter]

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "🚀 Starting SFTP test server..."
docker-compose -f "$PROJECT_ROOT/docker-compose.test.yml" up -d

echo "⏳ Waiting for SFTP server to be ready..."
# Wait up to 60 seconds for the server to be healthy
TIMEOUT=60
ELAPSED=0
while [ $ELAPSED -lt $TIMEOUT ]; do
    if docker-compose -f "$PROJECT_ROOT/docker-compose.test.yml" ps sftp | grep -q "healthy"; then
        echo "✅ SFTP server is ready"
        break
    fi
    sleep 2
    ELAPSED=$((ELAPSED + 2))
    echo "   Waiting... (${ELAPSED}s/${TIMEOUT}s)"
done

# Final check
if ! docker-compose -f "$PROJECT_ROOT/docker-compose.test.yml" ps sftp | grep -q "healthy"; then
    echo "❌ SFTP server failed to become healthy within ${TIMEOUT}s"
    echo "📋 Server logs:"
    docker-compose -f "$PROJECT_ROOT/docker-compose.test.yml" logs
    docker-compose -f "$PROJECT_ROOT/docker-compose.test.yml" down
    exit 1
fi

# Set environment variables for tests
export SFTP_TEST_HOST=localhost
export SFTP_TEST_PORT=2222
export SFTP_TEST_USER=testuser
export SFTP_TEST_PASS=testpass
export SFTP_TEST_ROOT=/home/testuser/upload

echo "🧪 Running tests..."
cd "$PROJECT_ROOT"

# Run tests with optional filter
if [ -n "$1" ]; then
    echo "   Filter: $1"
    dotnet test --verbosity normal --filter "$1"
else
    dotnet test --verbosity normal
fi

TEST_EXIT_CODE=$?

echo "🧹 Cleaning up..."
docker-compose -f "$PROJECT_ROOT/docker-compose.test.yml" down -v

if [ $TEST_EXIT_CODE -eq 0 ]; then
    echo "✅ All tests passed!"
else
    echo "❌ Tests failed with exit code $TEST_EXIT_CODE"
fi

exit $TEST_EXIT_CODE

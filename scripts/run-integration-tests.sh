#!/bin/bash

# Script to run SharpSync integration tests with Docker-based test servers (SFTP, FTP, S3/LocalStack)
# Usage: ./scripts/run-integration-tests.sh [test-filter]

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "🚀 Starting test servers (SFTP, FTP, S3/LocalStack)..."
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

# Final check for SFTP
if ! docker-compose -f "$PROJECT_ROOT/docker-compose.test.yml" ps sftp | grep -q "healthy"; then
    echo "❌ SFTP server failed to become healthy within ${TIMEOUT}s"
    echo "📋 Server logs:"
    docker-compose -f "$PROJECT_ROOT/docker-compose.test.yml" logs
    docker-compose -f "$PROJECT_ROOT/docker-compose.test.yml" down
    exit 1
fi

echo "⏳ Waiting for FTP server to be ready..."
ELAPSED=0
while [ $ELAPSED -lt $TIMEOUT ]; do
    if docker-compose -f "$PROJECT_ROOT/docker-compose.test.yml" ps ftp | grep -q "healthy"; then
        echo "✅ FTP server is ready"
        break
    fi
    sleep 2
    ELAPSED=$((ELAPSED + 2))
    echo "   Waiting... (${ELAPSED}s/${TIMEOUT}s)"
done

echo "⏳ Waiting for LocalStack (S3) to be ready..."
ELAPSED=0
while [ $ELAPSED -lt $TIMEOUT ]; do
    if docker-compose -f "$PROJECT_ROOT/docker-compose.test.yml" ps localstack | grep -q "healthy"; then
        echo "✅ LocalStack is ready"
        break
    fi
    sleep 2
    ELAPSED=$((ELAPSED + 2))
    echo "   Waiting... (${ELAPSED}s/${TIMEOUT}s)"
done

# Create S3 test bucket in LocalStack
echo "📦 Creating S3 test bucket..."
docker-compose -f "$PROJECT_ROOT/docker-compose.test.yml" exec -T localstack awslocal s3 mb s3://test-bucket 2>/dev/null || true

# Set environment variables for tests
export SFTP_TEST_HOST=localhost
export SFTP_TEST_PORT=2222
export SFTP_TEST_USER=testuser
export SFTP_TEST_PASS=testpass
export SFTP_TEST_ROOT=/home/testuser/upload

export FTP_TEST_HOST=localhost
export FTP_TEST_PORT=21
export FTP_TEST_USER=testuser
export FTP_TEST_PASS=testpass
export FTP_TEST_ROOT=/

export S3_TEST_BUCKET=test-bucket
export S3_TEST_ACCESS_KEY=test
export S3_TEST_SECRET_KEY=test
export S3_TEST_ENDPOINT=http://localhost:4566
export S3_TEST_PREFIX=sharpsync-tests

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

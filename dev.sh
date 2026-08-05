#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

if ! docker info >/dev/null 2>&1; then
    echo "Docker кор намекунад. Docker Desktop-ро сар кунед ва дубора кӯшиш кунед." >&2
    exit 1
fi

docker compose up -d

echo "Интизори омодагии PostgreSQL..."
timeout=30
elapsed=0
until docker compose exec -T postgres pg_isready -U office -d office >/dev/null 2>&1; do
    elapsed=$((elapsed + 1))
    if [ "$elapsed" -ge "$timeout" ]; then
        echo "PostgreSQL дар $timeout сония омода нашуд." >&2
        exit 1
    fi
    sleep 1
done

echo "PostgreSQL омода аст."

cd Office.Api
exec dotnet run

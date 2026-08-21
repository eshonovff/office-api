#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

PORT=5056

echo "== office-api dev =="
echo "Коммити ҷорӣ: $(git log -1 --format='%h %s' 2>/dev/null || echo '(git-и репо ёфт нашуд)')"
echo "Ветка: $(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')"
echo

# Санҷиши хатои имрӯза: агар процесси кӯҳна ҳанӯз ба порти 5056 гӯш карда бошад, "dotnet run"-и
# нав ё намеояд, ё браузер боз ба ҳамон бинарии кӯҳна мерасад — коди навсозишуда ҳеҷ гоҳ иҷро
# намешавад, бе ягон хатогии возеҳ. Чор бор имрӯз маҳз ҳамин сабаб буд.
existing_pids=$(lsof -ti "tcp:${PORT}" 2>/dev/null || true)
if [ -n "$existing_pids" ]; then
    echo "Порти $PORT аллакай банд аст (PID: $existing_pids) — процесси кӯҳна қатъ карда мешавад..."
    kill -9 $existing_pids 2>/dev/null || true
    sleep 1
fi

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

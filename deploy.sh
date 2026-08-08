#!/usr/bin/env bash
# Deploy/навсозии office-api дар сервер. Иҷро аз решаи репо (/opt/office/office-api).
set -euo pipefail

cd "$(dirname "$0")"

if [ ! -f .env ]; then
    echo "Хатогӣ: .env нест. Аввал: cp deploy/env.production.example .env ва пур кунед." >&2
    exit 1
fi

echo "==> git pull"
git pull --ff-only

echo "==> Docker image-ро сохтан ва контейнерҳоро сар додан"
docker compose -f docker-compose.prod.yml up -d --build

# shellcheck disable=SC1091
set -a; source .env; set +a

echo "==> Интизори саломатии API (аз host, порти ${API_PORT})..."
for _ in $(seq 1 30); do
    if curl -sf "http://127.0.0.1:${API_PORT}/health" >/dev/null 2>&1; then
        echo "API солим аст."
        docker compose -f docker-compose.prod.yml ps
        exit 0
    fi
    sleep 2
done

echo "Хатогӣ: API дар 60 сония саломат нашуд. Логро бинед:" >&2
docker compose -f docker-compose.prod.yml logs api --tail=100
exit 1

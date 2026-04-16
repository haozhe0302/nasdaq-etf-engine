#!/usr/bin/env bash
# Bootstrap Kafka topics for local development.
# Requires: docker compose infra running (kafka container healthy).
# Usage: ./scripts/bootstrap-kafka-topics.sh
#
# Idempotent — safe to run multiple times.

set -euo pipefail

BOOTSTRAP_SERVER="localhost:9092"
TOPIC_CMD="/opt/kafka/bin/kafka-topics.sh"

create_topic() {
    local name="$1"
    local partitions="${2:-1}"
    local cleanup="${3:-delete}"

    local config_args=(--config "cleanup.policy=$cleanup")
    if [ "$cleanup" = "compact" ]; then
        config_args+=(--config "min.cleanable.dirty.ratio=0.1")
        config_args+=(--config "segment.ms=100")
    fi

    echo "  Creating topic: $name (partitions=$partitions, cleanup=$cleanup)"

    docker compose exec -T kafka "$TOPIC_CMD" \
        --bootstrap-server "$BOOTSTRAP_SERVER" \
        --create \
        --if-not-exists \
        --topic "$name" \
        --partitions "$partitions" \
        --replication-factor 1 \
        "${config_args[@]}" 2>&1 | sed 's/^/    /' || \
        echo "  WARNING: topic $name may already exist or creation failed."
}

echo ""
echo "=== HQQQ Kafka Topic Bootstrap ==="
echo ""

if ! docker compose ps kafka >/dev/null 2>&1; then
    echo "ERROR: Kafka service 'kafka' not found."
    echo "Run this script from repo root and ensure 'docker compose up -d' has started infrastructure."
    exit 1
fi

create_topic "market.raw_ticks.v1"          3 "delete"
create_topic "market.latest_by_symbol.v1"   3 "compact"
create_topic "refdata.basket.active.v1"     1 "compact"
create_topic "refdata.basket.events.v1"     1 "delete"
create_topic "pricing.snapshots.v1"         1 "delete"
create_topic "ops.incidents.v1"             1 "delete"

echo ""
echo "=== Topic bootstrap complete ==="
echo ""

echo "Verifying topics:"
docker compose exec -T kafka "$TOPIC_CMD" --bootstrap-server "$BOOTSTRAP_SERVER" --list

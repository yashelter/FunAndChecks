#!/bin/sh
# Usage: sh monitoring/snapshot-existing-logs.sh NEW_DIRECTORY [CONTAINER]
# Additional local log directories may be copied into NEW_DIRECTORY manually.
set -eu
destination=${1:?Pass a new snapshot directory}
container=${2:-funandchecks-api}
if [ -e "$destination" ]; then
    echo "Snapshot directory already exists; refusing to overwrite." >&2
    exit 2
fi
running=$(docker inspect --format '{{.State.Running}}' "$container")
if [ "$running" = true ]; then
    echo "Stop the old API container before taking a complete snapshot." >&2
    exit 2
fi
mkdir -p "$destination"
for source in logs app/logs; do
    sub="$destination/$(printf '%s' "$source" | tr '/' '_')"
    mkdir -p "$sub"
    if docker cp "$container:/$source/." "$sub" 2>/dev/null; then :; else
        echo "No /$source directory in the old container; continuing." >&2
    fi
done
if ! find "$destination" -type f -name 'server-log-*.txt' | head -n 1 | grep -q .; then
    echo "No server-log-*.txt files found. Originals are unchanged." >&2
    exit 1
fi
echo "Snapshot copied to $destination. Preserve it until Grafana import is verified."


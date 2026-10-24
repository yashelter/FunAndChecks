#!/bin/sh
set -eu
# Remove only immutable imported snapshots, never original or live files.
# Require 14 days since the newest of original mtime and import ctime. An old
# snapshot cannot be removed while Alloy is still catching up after import.
hours=${TECHNICAL_LOG_RETENTION:-336h}
hours=${hours%h}
case "$hours" in ''|*[!0-9]*) echo 'Retention must be expressed in whole hours' >&2; exit 2 ;; esac
minutes=$((hours * 60 + 60))
find /app/logs/import -maxdepth 1 -type f -name 'server-log-*.txt' \
    -mmin "+$minutes" -cmin "+$minutes" -print -delete


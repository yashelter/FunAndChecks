#!/bin/bash
# Run only through the manual log-import Compose profile. /import is read-only.
set -eu
umask 027
target=/app/logs/import
mkdir -p "$target"
limit=${LOG_IMPORT_LIMIT_BYTES:-1073741824}
case "$limit" in ''|*[!0-9]*) echo "LOG_IMPORT_LIMIT_BYTES must be an integer" >&2; exit 2 ;; esac
used=$(du -sb "$target" | cut -f1)
count=0
# docker cp snapshots can contain more than one directory; never follow links.
listing=$(mktemp)
trap 'rm -f -- "$listing"' EXIT
find /import -type f -name 'server-log-*.txt' -print0 > "$listing"
if [ ! -s "$listing" ]; then
    echo 'No source logs found. Set LOG_IMPORT_DIRECTORY to the absolute snapshot path.' >&2
    exit 2
fi
while IFS= read -r -d '' source; do
    hash=$(sha256sum < "$source" | cut -d ' ' -f1)
    destination="$target/server-log-$hash.txt"
    if [ -e "$destination" ]; then
        continue
    fi
    size=$(stat -c %s "$source")
    if [ $((used + size)) -gt "$limit" ]; then
        echo "Import stopped: source snapshots exceed LOG_IMPORT_LIMIT_BYTES" >&2
        exit 1
    fi
    # Staging files cannot be discovered by Alloy until the atomic rename.
    cp -p -- "$source" "$target/.import-$hash"
    chmod 640 "$target/.import-$hash"
    mv -n -- "$target/.import-$hash" "$destination"
    used=$((used + size))
    count=$((count + 1))
done < "$listing"
echo "Imported $count immutable file snapshots ($used bytes). Originals untouched."


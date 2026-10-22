[CmdletBinding()]
param(
    [string] $Container = 'funandchecks-api',
    [string[]] $SourceDirectory = @(),
    [Parameter(Mandatory)] [string] $Destination
)
$ErrorActionPreference = 'Stop'

# This helper only copies. Stop the old container before calling it so its
# final record is complete; the helper never stops or recreates a container.
$target = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $target) {
    throw 'Destination already exists. Choose a new empty snapshot directory.'
}
$copied = 0
if ($Container) {
    $running = & docker inspect --format '{{.State.Running}}' $Container
    if ($LASTEXITCODE -ne 0) { throw "Cannot inspect container $Container" }
    if ($running -eq 'true') { throw 'Stop the old API container before taking a complete log snapshot.' }
}
New-Item -ItemType Directory -Path $target | Out-Null
if ($Container) {
    foreach ($source in @('/logs', '/app/logs')) {
        $sub = Join-Path $target ('container-' + $source.Replace('/', '_'))
        New-Item -ItemType Directory -Path $sub | Out-Null
        & docker cp "${Container}:${source}/." $sub 2>$null
        if ($LASTEXITCODE -eq 0) { $copied += @(Get-ChildItem -LiteralPath $sub -File -Filter 'server-log-*.txt' -Recurse).Count }
    }
}
foreach ($source in $SourceDirectory) {
    $resolved = (Resolve-Path -LiteralPath $source).Path
    $sub = Join-Path $target ('local-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $sub | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $resolved -File -Filter 'server-log-*.txt') {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $sub $file.Name)
        $copied++
    }
}
if ($copied -eq 0) { throw 'No server-log-*.txt files found. Originals have not been changed.' }
Write-Output "Copied $copied files to $target. Keep this snapshot until Grafana import is verified."


param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "publish",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$project = "src\CymaticLabs.InfluxDB.Studio\CymaticLabs.InfluxDB.Studio.csproj"

$args = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-o", $OutputDir
)
if ($NoRestore) {
    $args += "--no-restore"
}

& dotnet @args
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Get-ChildItem $OutputDir | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize

param(
    [ValidateSet("source", "docker")]
    [string]$Mode = "docker",
    [switch]$SkipBackup
)

$ErrorActionPreference = "Stop"

function Info([string]$msg) { Write-Host "[INFO] $msg" -ForegroundColor Cyan }
function Pass([string]$msg) { Write-Host "[OK] $msg" -ForegroundColor Green }
function Fail([string]$msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red; exit 1 }

$project = ".\src\DiscordXBot\DiscordXBot.csproj"
$startupProject = ".\src\DiscordXBot\DiscordXBot.csproj"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail "dotnet SDK is not installed"
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Fail "Docker CLI is not installed"
}

if ($Mode -eq "docker") {
    Info "Checking docker compose services for runtime migration mode"
    docker compose --profile prod ps | Out-Host
}

if (-not $SkipBackup) {
    if (-not (Test-Path .\backups)) {
        New-Item -ItemType Directory -Path .\backups | Out-Null
    }

    $stamp = Get-Date -Format yyyyMMdd_HHmmss
    $backupPath = ".\backups\discordbot_pre_migration_$stamp.sql"

    Info "Creating runtime DB backup at $backupPath"
    docker compose exec -T db pg_dump -U postgres -d discordbot > $backupPath
    Pass "Backup created"
}

Info "Applying EF migrations"
dotnet ef database update --project $project --startup-project $startupProject
Pass "Migrations applied"

Info "Verifying tracked_feeds schema"
$sql = "SELECT column_name FROM information_schema.columns WHERE table_name='tracked_feeds' ORDER BY ordinal_position;"
docker compose exec -T db psql -U postgres -d discordbot -c $sql | Out-Host

Pass "Migration apply completed"

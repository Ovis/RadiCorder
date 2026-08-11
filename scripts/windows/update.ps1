param(
    [string]$InstallDir = "$env:LOCALAPPDATA\RadiCorder",
    [string]$AppSourceDir = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path "publish"),
    [string]$ServiceName = "RadiCorder",
    [int]$HttpPort = 8085,
    [int]$StopTimeoutSec = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ExitCodeSuccess = 0
$ExitCodeAdmin = 10
$ExitCodeInstall = 11
$ExitCodeTaskRegister = 13
$ExitCodeTaskStart = 14
$ExitCodeHealth = 15
$ExitCodeUnexpected = 99

function Exit-WithCode {
    param([Parameter(Mandatory = $true)][int]$Code, [Parameter(Mandatory = $true)][string]$Message)
    Write-Host $Message
    exit $Code
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Ensure-ElevatedSelf {
    if (Test-IsAdministrator) { return }
    if ([string]::IsNullOrWhiteSpace($PSCommandPath)) { Exit-WithCode -Code $ExitCodeAdmin -Message "スクリプトパスを取得できませんでした。" }
    $shellPath = (Get-Process -Id $PID).Path
    if ([string]::IsNullOrWhiteSpace($shellPath)) { $shellPath = "pwsh.exe" }
    $args = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $PSCommandPath)
    if ($MyInvocation.UnboundArguments) { $args += $MyInvocation.UnboundArguments }
    Start-Process -FilePath $shellPath -Verb RunAs -ArgumentList $args | Out-Null
    exit $ExitCodeSuccess
}

function Stop-RadiCorderTaskForUpdate {
    param(
        [Parameter(Mandatory = $true)][string]$TaskName,
        [Parameter(Mandatory = $true)][int]$TimeoutSec
    )

    Write-Host "更新前にタスクを停止しています..."
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

    for ($i = 0; $i -lt $TimeoutSec; $i++) {
        Start-Sleep -Seconds 1
        $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        if ($null -eq $task) {
            Exit-WithCode -Code $ExitCodeTaskRegister -Message "タスク '$TaskName' が見つかりません。"
        }

        if ($task.State -ne "Running") {
            Write-Host "タスクを停止しました: $TaskName (State=$($task.State))"
            return
        }
    }

    Exit-WithCode -Code $ExitCodeTaskStart -Message "タスクがタイムアウトまでに停止しませんでした。"
}

try {
    Ensure-ElevatedSelf
    if (-not (Test-Path -Path $InstallDir)) { Exit-WithCode -Code $ExitCodeInstall -Message "InstallDir が見つかりません: $InstallDir" }
    if (-not (Test-Path -Path $AppSourceDir)) { Exit-WithCode -Code $ExitCodeInstall -Message "AppSourceDir が見つかりません: $AppSourceDir" }

    $task = Get-ScheduledTask -TaskName $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $task) { Exit-WithCode -Code $ExitCodeTaskRegister -Message "タスク '$ServiceName' が見つかりません。" }

    Stop-RadiCorderTaskForUpdate -TaskName $ServiceName -TimeoutSec $StopTimeoutSec

    $backupRoot = Join-Path $InstallDir "_backup"
    $backupDir = Join-Path $backupRoot (Get-Date -Format "yyyyMMdd-HHmmss")
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null

    $preserve = @("db", "record", "temp", "logs", "keys", "_backup")
    Get-ChildItem -Path $InstallDir -Force | ForEach-Object {
        if ($preserve -contains $_.Name) { return }
        Move-Item -Path $_.FullName -Destination (Join-Path $backupDir $_.Name) -Force
    }

    Copy-Item -Path (Join-Path $AppSourceDir "*") -Destination $InstallDir -Recurse -Force
    Write-Host "タスクを起動しています..."
    Start-ScheduledTask -TaskName $ServiceName

    $url = "http://127.0.0.1:$HttpPort/"
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Seconds 1
        try {
            $res = Invoke-WebRequest -Uri $url -Method Get -UseBasicParsing -TimeoutSec 3
            if ($res.StatusCode -ge 200 -and $res.StatusCode -lt 500) {
                Write-Host "更新が完了しました。"
                Write-Host "バックアップ先: $backupDir"
                exit $ExitCodeSuccess
            }
        }
        catch { }
    }

    Exit-WithCode -Code $ExitCodeHealth -Message "更新後のヘルスチェックに失敗しました: $url"
}
catch {
    Exit-WithCode -Code $ExitCodeUnexpected -Message ("想定外エラー: {0}" -f $_.Exception.Message)
}


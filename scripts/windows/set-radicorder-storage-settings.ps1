param(
    [Parameter(Mandatory = $true)]
    [string]$RecordDir,

    [string]$TempDir,

    [Alias('AppSettingsPath')]
    [string]$SettingsPath = ".\RadiCorder\radicorder.settings.json",

    [switch]$CreateDirectories,

    [switch]$RestartService,

    [string]$ServiceName = 'RadiCorder'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$recordKey = 'RecordFileSaveFolder'
$tempKey = 'TemporaryFileSaveFolder'
$radiKeepKey = 'RadiCorder'

function Get-DefaultSettingsObject {
    return [pscustomobject]@{
        RadiCorder = [pscustomobject]@{
            RecordFileSaveFolder = ''
            TemporaryFileSaveFolder = ''
            FfmpegExecutablePath = ''
        }
    }
}

function Load-OrInitializeSettings {
    param([Parameter(Mandatory = $true)][string]$TargetPath)

    if (Test-Path -Path $TargetPath) {
        $loaded = Get-Content -Raw -Path $TargetPath | ConvertFrom-Json
        if ($null -eq $loaded) {
            throw "JSON の解析に失敗しました: $TargetPath"
        }
        return $loaded
    }

    $baseDir = Split-Path -Path $TargetPath -Parent
    $samplePath = Join-Path $baseDir 'radicorder.settings.sample.json'
    if (Test-Path -Path $samplePath) {
        $loaded = Get-Content -Raw -Path $samplePath | ConvertFrom-Json
        if ($null -eq $loaded) {
            throw "JSON の解析に失敗しました: $samplePath"
        }
        return $loaded
    }

    return Get-DefaultSettingsObject
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Ensure-ElevatedSelf {
    if (Test-IsAdministrator) {
        return
    }

    # Only needed when service restart is requested.
    Write-Host "管理者権限で set-radicorder-storage-settings.ps1 を再実行します..."

    if ([string]::IsNullOrWhiteSpace($PSCommandPath)) {
        throw "スクリプトパスを取得できませんでした。スクリプトファイルとして実行してください。"
    }

    $shellPath = (Get-Process -Id $PID).Path
    if ([string]::IsNullOrWhiteSpace($shellPath)) {
        $shellPath = "pwsh.exe"
    }

    $argumentList = @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $PSCommandPath
    )

    if ($MyInvocation.UnboundArguments) {
        $argumentList += $MyInvocation.UnboundArguments
    }

    Start-Process -FilePath $shellPath -Verb RunAs -ArgumentList $argumentList | Out-Null
    exit 0
}

if ($CreateDirectories) {
    New-Item -ItemType Directory -Path $RecordDir -Force | Out-Null
    if (-not [string]::IsNullOrWhiteSpace($TempDir)) {
        New-Item -ItemType Directory -Path $TempDir -Force | Out-Null
    }
}

# Load and patch only the RadiCorder storage keys.
$settings = Load-OrInitializeSettings -TargetPath $SettingsPath
if (-not ($settings.PSObject.Properties.Name -contains $radiKeepKey)) {
    $settings | Add-Member -MemberType NoteProperty -Name $radiKeepKey -Value ([pscustomobject]@{})
}

$settings.RadiCorder | Add-Member -MemberType NoteProperty -Name $recordKey -Value $RecordDir -Force
if (-not [string]::IsNullOrWhiteSpace($TempDir)) {
    $settings.RadiCorder | Add-Member -MemberType NoteProperty -Name $tempKey -Value $TempDir -Force
}

$json = $settings | ConvertTo-Json -Depth 10
Set-Content -Path $SettingsPath -Value $json -Encoding UTF8

Write-Host "radicorder.settings.json を更新しました: $SettingsPath"
Write-Host "RadiCorder.$recordKey = $RecordDir"
if (-not [string]::IsNullOrWhiteSpace($TempDir)) {
    Write-Host "RadiCorder.$tempKey = $TempDir"
}

if ($RestartService) {
    # Service restart applies updated settings without manual operation.
    Ensure-ElevatedSelf

    $task = Get-ScheduledTask -TaskName $ServiceName -ErrorAction SilentlyContinue
    if ($null -ne $task) {
        Stop-ScheduledTask -TaskName $ServiceName -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
        Start-ScheduledTask -TaskName $ServiceName
        Write-Host "タスク '$ServiceName' を再起動しました。"
        return
    }

    throw "タスク '$ServiceName' が見つかりません。"
}


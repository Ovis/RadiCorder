param(
    [string]$InstallDir = "$env:LOCALAPPDATA\RadiCorder",
    [string]$ServiceName = "RadiCorder",
    [switch]$RemoveData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ExitCodeSuccess = 0
$ExitCodeAdmin = 10
$ExitCodeInstall = 11
$ExitCodeTaskRegister = 13
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

try {
    Ensure-ElevatedSelf

    $task = Get-ScheduledTask -TaskName $ServiceName -ErrorAction SilentlyContinue
    if ($null -ne $task) {
        Stop-ScheduledTask -TaskName $ServiceName -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $ServiceName -Confirm:$false -ErrorAction SilentlyContinue
        Write-Host "タスクを削除しました: $ServiceName"
    }
    else {
        Write-Host "タスク '$ServiceName' は存在しません。"
    }

    if (-not (Test-Path -Path $InstallDir)) {
        Write-Host "インストールディレクトリが見つかりません: $InstallDir"
        exit $ExitCodeSuccess
    }

    $dataDirs = @("db", "record", "temp", "logs", "keys")
    if ($RemoveData) {
        Remove-Item -Path $InstallDir -Recurse -Force
        Write-Host "データを含めてインストールディレクトリを削除しました: $InstallDir"
    }
    else {
        Get-ChildItem -Path $InstallDir -Force | ForEach-Object {
            if ($dataDirs -notcontains $_.Name) {
                Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        Write-Host "バイナリとスクリプトのみ削除しました。データディレクトリは保持しました。"
        Write-Host "保持対象: $($dataDirs -join ', ')"
    }

    exit $ExitCodeSuccess
}
catch {
    Exit-WithCode -Code $ExitCodeUnexpected -Message ("想定外エラー: {0}" -f $_.Exception.Message)
}


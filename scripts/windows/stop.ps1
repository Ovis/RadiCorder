param(
    [string]$ServiceName = "RadiCorder",
    [int]$TimeoutSec = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ExitCodeSuccess = 0
$ExitCodeAdmin = 10
$ExitCodeTaskRegister = 13
$ExitCodeTaskStart = 14
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
    if ($null -eq $task) { Exit-WithCode -Code $ExitCodeTaskRegister -Message "タスク '$ServiceName' が見つかりません。" }

    Stop-ScheduledTask -TaskName $ServiceName -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt $TimeoutSec; $i++) {
        Start-Sleep -Seconds 1
        $state = (Get-ScheduledTask -TaskName $ServiceName).State
        if ($state -ne "Running") {
            Write-Host "タスクを停止しました: $ServiceName"
            exit $ExitCodeSuccess
        }
    }

    Exit-WithCode -Code $ExitCodeTaskStart -Message "タスクがタイムアウトまでに停止しませんでした。"
}
catch {
    Exit-WithCode -Code $ExitCodeUnexpected -Message ("想定外エラー: {0}" -f $_.Exception.Message)
}


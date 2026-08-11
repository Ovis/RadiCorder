param(
    [string]$ServiceName = "RadiCorder",
    [int]$HttpPort = 8085,
    [int]$HealthTimeoutSec = 40
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ExitCodeSuccess = 0
$ExitCodeAdmin = 10
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

function Format-TaskResultCode {
    param([Parameter(Mandatory = $true)]$Code)
    try {
        $raw64 = [int64]$Code
        $unsigned = [uint32]($raw64 -band 0xFFFFFFFFL)
        $signed = [int32]$unsigned
        return ("{0} (0x{1})" -f $signed, $unsigned.ToString("X8"))
    }
    catch {
        return ("{0}" -f $Code)
    }
}

function Get-PrimaryIPv4Address {
    try {
        $candidates = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object {
                $_.IPAddress -notlike '127.*' -and
                $_.IPAddress -notlike '169.254.*' -and
                $_.PrefixOrigin -ne 'WellKnown'
            } |
            Sort-Object -Property InterfaceMetric, SkipAsSource

        $selected = $candidates | Select-Object -First 1
        if ($null -ne $selected -and -not [string]::IsNullOrWhiteSpace($selected.IPAddress)) {
            return $selected.IPAddress
        }
    }
    catch { }
    return $null
}

try {
    Ensure-ElevatedSelf
    $task = Get-ScheduledTask -TaskName $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $task) { Exit-WithCode -Code $ExitCodeTaskRegister -Message "タスク '$ServiceName' が見つかりません。" }

    Start-ScheduledTask -TaskName $ServiceName

    $url = "http://127.0.0.1:$HttpPort/"
    for ($i = 0; $i -lt $HealthTimeoutSec; $i++) {
        Start-Sleep -Seconds 1
        $info = Get-ScheduledTaskInfo -TaskName $ServiceName -ErrorAction SilentlyContinue
        if ($null -ne $info) {
            $state = (Get-ScheduledTask -TaskName $ServiceName -ErrorAction SilentlyContinue).State
            if ($state -eq "Ready" -and $info.LastTaskResult -ne 0) {
                Exit-WithCode -Code $ExitCodeTaskStart -Message ("タスクが即終了しました。LastTaskResult={0}" -f (Format-TaskResultCode -Code $info.LastTaskResult))
            }
        }
        try {
            $res = Invoke-WebRequest -Uri $url -Method Get -UseBasicParsing -TimeoutSec 3
            if ($res.StatusCode -ge 200 -and $res.StatusCode -lt 500) {
                $lanIp = Get-PrimaryIPv4Address
                Write-Host "タスクを起動しました: $ServiceName"
                Write-Host "アクセスURL(ローカル): http://127.0.0.1:$HttpPort"
                if (-not [string]::IsNullOrWhiteSpace($lanIp)) {
                    Write-Host "アクセスURL(LAN): http://$lanIp`:$HttpPort"
                }
                exit $ExitCodeSuccess
            }
        }
        catch { }
    }

    $finalInfo = Get-ScheduledTaskInfo -TaskName $ServiceName -ErrorAction SilentlyContinue
    if ($null -ne $finalInfo) {
        Exit-WithCode -Code $ExitCodeHealth -Message ("ヘルスチェックに失敗しました: {0} / LastTaskResult={1}" -f $url, (Format-TaskResultCode -Code $finalInfo.LastTaskResult))
    }
    Exit-WithCode -Code $ExitCodeHealth -Message "ヘルスチェックに失敗しました: $url"
}
catch {
    Exit-WithCode -Code $ExitCodeUnexpected -Message ("想定外エラー: {0}" -f $_.Exception.Message)
}


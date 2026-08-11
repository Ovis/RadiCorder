param(
    [string]$InstallDir = "$env:LOCALAPPDATA\RadiCorder",
    [string]$AppSourceDir = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path "publish"),
    [string]$ServiceName = "RadiCorder",
    [string]$ServiceDisplayName = "RadiCorder",
    [string]$ServiceDescription = "RadiCorder",
    [int]$HttpPort = 8085,
    [string]$RecordDir,
    [string]$TempDir,
    [bool]$UseWingetForFfmpeg = $true,
    [bool]$UseDirectDownloadForFfmpeg = $true,
    [bool]$UseChocolateyForFfmpeg = $true,
    [ValidateSet("CheckOnly", "Skip")]
    [string]$DotNetRuntimeMode = "CheckOnly",
    [string]$FfmpegPath,
    [switch]$SkipServiceStart,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# 終了コードはインストーラー呼び出し側で結果判定に使うため、用途別に固定値で管理する。
$ExitCodeSuccess = 0
$ExitCodeAdmin = 10
$ExitCodeInstall = 11
$ExitCodeFfmpeg = 12
$ExitCodeTaskRegister = 13
$ExitCodeTaskStart = 14
$ExitCodeHealth = 15
$ExitCodeUnexpected = 99

$script:LogFilePath = Join-Path $env:TEMP ("radicorder-install-bootstrap-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))

# 統一ログ出力（標準出力 + ログファイル）
function Write-Log {
    param([Parameter(Mandatory = $true)][string]$Message)
    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Message
    Write-Host $line
    if (-not [string]::IsNullOrWhiteSpace($script:LogFilePath)) {
        Add-Content -Path $script:LogFilePath -Value $line -Encoding UTF8
    }
}

function Switch-LogFile {
    param([Parameter(Mandatory = $true)][string]$NewLogPath)
    try {
        $oldPath = $script:LogFilePath
        $script:LogFilePath = $NewLogPath
        Write-Log ("ログ出力先を切り替えました: {0}" -f $NewLogPath)
        if (-not [string]::IsNullOrWhiteSpace($oldPath) -and (Test-Path -Path $oldPath)) {
            Add-Content -Path $script:LogFilePath -Value ("[INFO] bootstrap log: {0}" -f $oldPath) -Encoding UTF8
        }
    }
    catch {
        # ログ切り替え失敗時は bootstrap ログへ継続出力
        $script:LogFilePath = $oldPath
        Write-Log ("ログ出力先の切り替えに失敗しました: {0}" -f $_.Exception.Message)
    }
}

function Exit-WithCode {
    param(
        [Parameter(Mandatory = $true)][int]$Code,
        [Parameter(Mandatory = $true)][string]$Message
    )
    Write-Log $Message
    exit $Code
}

# UAC 昇格が必要な処理（タスク登録 / Firewall 変更）を含むため管理者判定を行う。
function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# 非管理者実行時は同一スクリプトを管理者権限で再起動して処理を引き継ぐ。
function Ensure-ElevatedSelf {
    param([Parameter(Mandatory = $true)][int]$OnFailExitCode)
    if (Test-IsAdministrator) { return }

    Write-Host "管理者権限で install.ps1 を再実行します..."
    if ([string]::IsNullOrWhiteSpace($PSCommandPath)) {
        Exit-WithCode -Code $OnFailExitCode -Message "スクリプトパスを取得できませんでした。"
    }

    $shellPath = (Get-Process -Id $PID).Path
    if ([string]::IsNullOrWhiteSpace($shellPath)) { $shellPath = "pwsh.exe" }

    $argumentList = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $PSCommandPath)
    if ($MyInvocation.UnboundArguments) { $argumentList += $MyInvocation.UnboundArguments }

    try {
        Start-Process -FilePath $shellPath -Verb RunAs -ArgumentList $argumentList | Out-Null
        exit $ExitCodeSuccess
    }
    catch {
        Exit-WithCode -Code $OnFailExitCode -Message ("管理者昇格に失敗したか、UAC がキャンセルされました: {0}" -f $_.Exception.Message)
    }
}

# 配布形式（exe / dll）どちらでも起動できるよう、実体を判定して返す。
function Resolve-AppEntryPath {
    param([Parameter(Mandatory = $true)][string]$BaseDir)
    $exePath = Join-Path $BaseDir "RadiCorder.exe"
    if (Test-Path -Path $exePath) { return $exePath }
    $dllPath = Join-Path $BaseDir "RadiCorder.dll"
    if (Test-Path -Path $dllPath) { return $dllPath }
    throw "インストール先に RadiCorder.exe / RadiCorder.dll が見つかりません: $BaseDir"
}

# 1) winget 経由インストール
function Install-FfmpegByWinget {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($null -eq $winget) {
        Write-Log "winget が見つかりません。"
        return $false
    }

    $installArgs = @(
        "install", "--id", "Gyan.FFmpeg", "-e",
        "--source", "winget",
        "--accept-package-agreements", "--accept-source-agreements",
        "--disable-interactivity"
    )

    Write-Log "winget で ffmpeg 導入を試行します..."
    & winget @installArgs
    if ($LASTEXITCODE -eq 0) { return $true }

    & winget source reset --force
    & winget source update
    & winget @installArgs
    return ($LASTEXITCODE -eq 0)
}

# 2) 直接ダウンロード（winget が使えない環境向け）
function Install-FfmpegByDirectDownload {
    param([Parameter(Mandatory = $true)][string]$TargetInstallDir)
    $urls = @(
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-git-essentials.zip"
    )
    $workDir = Join-Path $env:TEMP ("radicorder-ffmpeg-{0}" -f ([Guid]::NewGuid().ToString("N")))
    New-Item -ItemType Directory -Path $workDir -Force | Out-Null
    try {
        foreach ($url in $urls) {
            try {
                $zipPath = Join-Path $workDir "ffmpeg.zip"
                Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing -TimeoutSec 120
                $extractDir = Join-Path $workDir "extract"
                Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force
                $ffmpegExe = Get-ChildItem -Path $extractDir -Recurse -Filter "ffmpeg.exe" -File | Select-Object -First 1
                if ($null -eq $ffmpegExe) { continue }
                Copy-Item -Path $ffmpegExe.FullName -Destination (Join-Path $TargetInstallDir "ffmpeg.exe") -Force
                return $true
            }
            catch { continue }
        }
    }
    finally {
        if (Test-Path -Path $workDir) { Remove-Item -Path $workDir -Recurse -Force -ErrorAction SilentlyContinue }
    }
    return $false
}

# 3) Chocolatey 経由
function Install-FfmpegByChocolatey {
    $choco = Get-Command choco -ErrorAction SilentlyContinue
    if ($null -eq $choco) { return $false }
    & choco install ffmpeg -y --no-progress
    return ($LASTEXITCODE -eq 0)
}

# PATH 解決 + winget 配下探索で ffmpeg 実体を特定する。
function Resolve-FfmpegExecutablePath {
    $cmd = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($null -ne $cmd -and -not [string]::IsNullOrWhiteSpace($cmd.Source) -and (Test-Path -Path $cmd.Source)) {
        return $cmd.Source
    }

    $userWinGetPackages = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages"
    if (Test-Path -Path $userWinGetPackages) {
        $candidate = Get-ChildItem -Path $userWinGetPackages -Recurse -Filter "ffmpeg.exe" -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $candidate) { return $candidate.FullName }
    }

    return $null
}

# アプリが読む設定ファイルに ffmpeg の絶対パスを保存する。
function Get-DefaultRadiCorderSettingsObject {
    return [pscustomobject]@{
        RadiCorder = [pscustomobject]@{
            RecordFileSaveFolder = ''
            TemporaryFileSaveFolder = ''
            FfmpegExecutablePath = ''
            DbDirectory = 'db'
            LogDirectory = 'logs'
            DataProtectionKeysPath = 'keys'
        }
    }
}

function Load-OrInitializeRadiCorderSettings {
    param([Parameter(Mandatory = $true)][string]$SettingsPath)

    if (Test-Path -Path $SettingsPath) {
        $json = Get-Content -Raw -Path $SettingsPath | ConvertFrom-Json
        if ($null -eq $json) {
            Exit-WithCode -Code $ExitCodeInstall -Message "radicorder.settings.json の読み込みに失敗しました。"
        }
        return $json
    }

    $baseDir = Split-Path -Path $SettingsPath -Parent
    $samplePath = Join-Path $baseDir "radicorder.settings.sample.json"
    if (Test-Path -Path $samplePath) {
        $json = Get-Content -Raw -Path $samplePath | ConvertFrom-Json
        if ($null -eq $json) {
            Exit-WithCode -Code $ExitCodeInstall -Message "radicorder.settings.sample.json の読み込みに失敗しました。"
        }
        return $json
    }

    return Get-DefaultRadiCorderSettingsObject
}

function Set-FfmpegExecutablePathInSettings {
    param(
        [Parameter(Mandatory = $true)][string]$SettingsPath,
        [Parameter(Mandatory = $true)][string]$ExecutablePath
    )

    $json = Load-OrInitializeRadiCorderSettings -SettingsPath $SettingsPath
    if (-not ($json.PSObject.Properties.Name -contains "RadiCorder")) {
        $json | Add-Member -MemberType NoteProperty -Name "RadiCorder" -Value ([pscustomobject]@{})
    }

    $json.RadiCorder | Add-Member -MemberType NoteProperty -Name "FfmpegExecutablePath" -Value $ExecutablePath -Force
    $out = $json | ConvertTo-Json -Depth 15
    [System.IO.File]::WriteAllText($SettingsPath, $out, [System.Text.UTF8Encoding]::new($false))
    Write-Log ("RadiCorder.FfmpegExecutablePath を設定しました: {0}" -f $ExecutablePath)
}

function Ensure-Ffmpeg {
    param(
        [Parameter(Mandatory = $true)][string]$TargetInstallDir,
        [Parameter(Mandatory = $true)][string]$SettingsPath,
        [Parameter(Mandatory = $true)][bool]$WingetEnabled,
        [Parameter(Mandatory = $true)][bool]$DirectDownloadEnabled,
        [Parameter(Mandatory = $true)][bool]$ChocolateyEnabled,
        [string]$ExplicitFfmpegPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitFfmpegPath)) {
        # 明示指定がある場合は自動導入を行わず、そのパスを最優先で採用する
        if (-not (Test-Path -Path $ExplicitFfmpegPath)) { Exit-WithCode -Code $ExitCodeFfmpeg -Message "指定された -FfmpegPath が見つかりません。" }
        Set-FfmpegExecutablePathInSettings -SettingsPath $SettingsPath -ExecutablePath $ExplicitFfmpegPath
        return
    }

    $existingPath = Resolve-FfmpegExecutablePath
    if (-not [string]::IsNullOrWhiteSpace($existingPath)) {
        # 既存導入済み ffmpeg が見つかれば追加インストールは行わない
        Set-FfmpegExecutablePathInSettings -SettingsPath $SettingsPath -ExecutablePath $existingPath
        return
    }

    # 導入方法は失敗しても次の候補へフォールバックする（winget -> 直接DL -> Chocolatey -> 同梱）
    if ($WingetEnabled -and (Install-FfmpegByWinget)) {
        $wingetPath = Resolve-FfmpegExecutablePath
        if (-not [string]::IsNullOrWhiteSpace($wingetPath)) {
            Set-FfmpegExecutablePathInSettings -SettingsPath $SettingsPath -ExecutablePath $wingetPath
            return
        }
    }
    if ($DirectDownloadEnabled -and (Install-FfmpegByDirectDownload -TargetInstallDir $TargetInstallDir)) {
        $bundled = Join-Path $TargetInstallDir "ffmpeg.exe"
        if (Test-Path -Path $bundled) {
            Set-FfmpegExecutablePathInSettings -SettingsPath $SettingsPath -ExecutablePath $bundled
            return
        }
    }
    if ($ChocolateyEnabled -and (Install-FfmpegByChocolatey)) {
        $chocoPath = Resolve-FfmpegExecutablePath
        if (-not [string]::IsNullOrWhiteSpace($chocoPath)) {
            Set-FfmpegExecutablePathInSettings -SettingsPath $SettingsPath -ExecutablePath $chocoPath
            return
        }
    }

    $bundledExe = Join-Path $TargetInstallDir "ffmpeg.exe"
    if (Test-Path -Path $bundledExe) {
        Set-FfmpegExecutablePathInSettings -SettingsPath $SettingsPath -ExecutablePath $bundledExe
        return
    }

    Exit-WithCode -Code $ExitCodeFfmpeg -Message "ffmpeg を導入できませんでした。"
}

# runtimeconfig から必要な共有フレームワーク（メジャーバージョン）を抽出する。
# 解析失敗時は既定値（NETCore/App 10.0）で安全側に倒す。
function Get-RequiredDotNetRuntimeRequirements {
    param([Parameter(Mandatory = $true)][string]$TargetInstallDir)

    $requirements = @{}
    $runtimeConfigPath = Join-Path $TargetInstallDir "RadiCorder.runtimeconfig.json"

    function Add-Requirement {
        param(
            [hashtable]$Map,
            [string]$Name,
            [string]$VersionText
        )

        if ([string]::IsNullOrWhiteSpace($Name)) { return }

        $major = 0
        if (-not [string]::IsNullOrWhiteSpace($VersionText)) {
            try {
                $major = [int]($VersionText.Split('.')[0])
            }
            catch {
                $major = 0
            }
        }
        if ($major -le 0) { return }

        if ($Map.ContainsKey($Name)) {
            if ($major -gt $Map[$Name].Major) {
                $Map[$Name] = [pscustomobject]@{ Name = $Name; Major = $major; Version = $VersionText }
            }
            return
        }

        $Map[$Name] = [pscustomobject]@{ Name = $Name; Major = $major; Version = $VersionText }
    }

    if (Test-Path -Path $runtimeConfigPath) {
        try {
            $json = Get-Content -Raw -Path $runtimeConfigPath | ConvertFrom-Json
            $runtimeOptions = $json.runtimeOptions

            # 単一 framework 定義（旧形式）
            $frameworkProp = $runtimeOptions.PSObject.Properties["framework"]
            if ($null -ne $frameworkProp -and $null -ne $frameworkProp.Value) {
                Add-Requirement -Map $requirements -Name $frameworkProp.Value.name -VersionText $frameworkProp.Value.version
            }

            # 複数 frameworks 定義（新形式）
            $frameworksProp = $runtimeOptions.PSObject.Properties["frameworks"]
            if ($null -ne $frameworksProp -and $null -ne $frameworksProp.Value) {
                foreach ($fw in @($frameworksProp.Value)) {
                    Add-Requirement -Map $requirements -Name $fw.name -VersionText $fw.version
                }
            }
        }
        catch {
            Write-Log ("runtimeconfig の解析に失敗したため、既定要件で判定します: {0}" -f $_.Exception.Message)
        }
    }

    if ($requirements.Count -eq 0) {
        # runtimeconfig 読み取り不可時の安全側既定値
        Add-Requirement -Map $requirements -Name "Microsoft.NETCore.App" -VersionText "10.0.0"
        Add-Requirement -Map $requirements -Name "Microsoft.AspNetCore.App" -VersionText "10.0.0"
    }

    return @($requirements.Values)
}

# dotnet コマンドの実体を解決（PATH 優先、既定インストール先をフォールバック）
function Get-DotNetHostPath {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $dotnet -and -not [string]::IsNullOrWhiteSpace($dotnet.Source)) {
        return $dotnet.Source
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles "dotnet\dotnet.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "dotnet\dotnet.exe")
    )
    foreach ($path in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -Path $path)) {
            return $path
        }
    }
    return $null
}

function Get-InstalledDotNetRuntimes {
    param([Parameter(Mandatory = $true)][string]$DotNetPath)

    try {
        return @(& $DotNetPath --list-runtimes 2>$null)
    }
    catch {
        return $null
    }
}

# --list-runtimes の行を見て「指定フレームワークの指定メジャーが存在するか」を判定。
function Test-DotNetRuntimeInstalled {
    param(
        [Parameter(Mandatory = $true)][string]$FrameworkName,
        [Parameter(Mandatory = $true)][int]$MajorVersion,
        [Parameter(Mandatory = $true)][string[]]$InstalledRuntimeLines
    )

    $escapedName = [Regex]::Escape($FrameworkName)
    foreach ($line in $InstalledRuntimeLines) {
        if ($line -match ("^{0}\s+{1}\." -f $escapedName, $MajorVersion)) {
            return $true
        }
    }
    return $false
}

# Framework-dependent 版向けに、配布前にランタイム要件を満たしているか検証する。
function Ensure-DotNetRuntime {
    param(
        [Parameter(Mandatory = $true)][string]$TargetInstallDir,
        [Parameter(Mandatory = $true)][string]$Mode
    )

    if ($Mode -eq "Skip") {
        Write-Log ".NET ランタイムチェックをスキップします（Self-contained 想定）。"
        return
    }

    $requiredFrameworks = @(Get-RequiredDotNetRuntimeRequirements -TargetInstallDir $TargetInstallDir)
    $dotnetPath = Get-DotNetHostPath
    if ([string]::IsNullOrWhiteSpace($dotnetPath)) {
        Exit-WithCode -Code $ExitCodeInstall -Message ".NET ホスト (dotnet.exe) が見つかりません。先に .NET をインストールしてください。"
    }
    Write-Log ("dotnet 検出パス: {0}" -f $dotnetPath)

    $installedRuntimes = Get-InstalledDotNetRuntimes -DotNetPath $dotnetPath
    if ($null -eq $installedRuntimes) {
        Exit-WithCode -Code $ExitCodeInstall -Message ("dotnet --list-runtimes の実行に失敗しました。dotnet パス: {0}" -f $dotnetPath)
    }
    $installedRuntimes = @($installedRuntimes)
    if ($installedRuntimes.Count -eq 0) {
        Write-Log "dotnet --list-runtimes の結果が0件です。共有フレームワークが未導入の可能性があります。"
    }

    foreach ($fw in $requiredFrameworks) {
        Write-Log ("必要ランタイム: {0} {1}.x" -f $fw.Name, $fw.Major)
    }

    $missing = @()
    foreach ($fw in $requiredFrameworks) {
        $installed = Test-DotNetRuntimeInstalled -FrameworkName $fw.Name -MajorVersion $fw.Major -InstalledRuntimeLines $installedRuntimes
        if (-not $installed) {
            $missing += ("{0} {1}.x" -f $fw.Name, $fw.Major)
        }
    }

    if ($missing.Count -eq 0) {
        Write-Log ".NET ランタイムは必要要件を満たしています。"
        return
    }

    $missingText = ($missing -join ", ")
    Exit-WithCode -Code $ExitCodeInstall -Message ("必要な .NET 共有フレームワークが不足しています: {0}。ASP.NET Core Runtime を含めて導入後に再実行してください。" -f $missingText)
}

# SYSTEM 権限の「起動時実行」タスクとして登録する。
function Register-RadiCorderScheduledTask {
    param(
        [Parameter(Mandatory = $true)][string]$TaskName,
        [Parameter(Mandatory = $true)][string]$ExecutePath,
        [Parameter(Mandatory = $true)][string]$ArgumentText,
        [Parameter(Mandatory = $true)][string]$WorkingDir,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $action = New-ScheduledTaskAction -Execute $ExecutePath -Argument $ArgumentText -WorkingDirectory $WorkingDir
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -StartWhenAvailable
    $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -RunLevel Highest -LogonType ServiceAccount
    $task = New-ScheduledTask -Action $action -Principal $principal -Trigger $trigger -Settings $settings -Description $Description
    Register-ScheduledTask -TaskName $TaskName -InputObject $task -Force | Out-Null
}

# タスク実行結果コードを10進/16進で整形し、障害解析しやすくする。
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

# インストール完了時の案内表示用に、利用可能な代表 IPv4 を推定する。
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

# LAN アクセス想定のため、指定ポートの受信許可ルールを追加する。
function Ensure-RadiCorderFirewallRule {
    param([Parameter(Mandatory = $true)][int]$Port)

    $ruleName = "RadiCorder TCP $Port"
    $existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    if ($null -ne $existing) {
        Write-Log ("Windows Firewall 受信規則は既に存在します: {0}" -f $ruleName)
        return
    }

    New-NetFirewallRule `
        -DisplayName $ruleName `
        -Direction Inbound `
        -Action Allow `
        -Protocol TCP `
        -LocalPort $Port `
        -Profile Private,Domain `
        -Program Any | Out-Null

    Write-Log ("Windows Firewall 受信規則を追加しました: {0}" -f $ruleName)
}

# タスク起動後にヘルスチェックを行い、「登録だけ成功で実行失敗」を検出する。
function Start-RadiCorderTaskAndCheckHealth {
    param(
        [Parameter(Mandatory = $true)][string]$TaskName,
        [Parameter(Mandatory = $true)][int]$Port
    )

    Start-ScheduledTask -TaskName $TaskName
    $healthUrl = "http://127.0.0.1:$Port/"
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Seconds 1

        $taskInfo = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction SilentlyContinue
        if ($null -ne $taskInfo) {
            $taskState = (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue).State
            if ($taskState -eq "Ready" -and $taskInfo.LastTaskResult -ne 0) {
                Exit-WithCode -Code $ExitCodeTaskStart -Message ("タスクが即終了しました。LastTaskResult={0}" -f (Format-TaskResultCode -Code $taskInfo.LastTaskResult))
            }
        }

        try {
            $res = Invoke-WebRequest -Uri $healthUrl -Method Get -UseBasicParsing -TimeoutSec 3
            if ($res.StatusCode -ge 200 -and $res.StatusCode -lt 500) { return }
        }
        catch { }
    }
    $finalInfo = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($null -ne $finalInfo) {
        Exit-WithCode -Code $ExitCodeHealth -Message ("ヘルスチェックに失敗しました: {0} / LastTaskResult={1}" -f $healthUrl, (Format-TaskResultCode -Code $finalInfo.LastTaskResult))
    }
    Exit-WithCode -Code $ExitCodeHealth -Message "ヘルスチェックに失敗しました: $healthUrl"
}

try {
    # -------------------------------
    # 1. 事前検証・初期化
    # -------------------------------
    Write-Log "install.ps1 を開始しました。"
    if ($PSVersionTable.PSVersion.Major -lt 5) { Exit-WithCode -Code $ExitCodeUnexpected -Message "PowerShell 5.1 以上が必要です。" }
    Ensure-ElevatedSelf -OnFailExitCode $ExitCodeAdmin

    if (-not (Test-Path -Path $AppSourceDir)) { Exit-WithCode -Code $ExitCodeInstall -Message "AppSourceDir が見つかりません: $AppSourceDir" }
    if ([string]::IsNullOrWhiteSpace($RecordDir)) { $RecordDir = Join-Path $InstallDir "record" }
    if ([string]::IsNullOrWhiteSpace($TempDir)) { $TempDir = Join-Path $InstallDir "temp" }

    # -------------------------------
    # 2. ログ/ディレクトリ準備
    # -------------------------------
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    $logDir = Join-Path $InstallDir "logs"
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    $installLogPath = Join-Path $logDir ("install-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
    Switch-LogFile -NewLogPath $installLogPath

    Write-Log "RadiCorder のインストールを開始します。"
    Write-Log ("インストール先: {0}" -f $InstallDir)
    Write-Log ("配置元ディレクトリ: {0}" -f $AppSourceDir)

    $dirs = @((Join-Path $InstallDir "db"), $RecordDir, $TempDir, (Join-Path $InstallDir "logs"), (Join-Path $InstallDir "keys"))
    foreach ($d in $dirs) { New-Item -ItemType Directory -Path $d -Force | Out-Null }

    # -------------------------------
    # 3. 既存タスク確認
    # -------------------------------
    $existingTask = Get-ScheduledTask -TaskName $ServiceName -ErrorAction SilentlyContinue
    if ($null -ne $existingTask -and -not $Force) {
        Exit-WithCode -Code $ExitCodeTaskRegister -Message "タスク '$ServiceName' は既に存在します。-Force を指定してください。"
    }

    # -------------------------------
    # 4. .NET 要件確認（fd 版向け）
    # -------------------------------
    # アプリ展開前にランタイム要件を確認する。
    Ensure-DotNetRuntime -TargetInstallDir $AppSourceDir -Mode $DotNetRuntimeMode

    # -------------------------------
    # 5. アプリ展開
    # -------------------------------
    $resolvedSourceDir = (Resolve-Path -Path $AppSourceDir).Path
    $resolvedInstallDir = (Resolve-Path -Path $InstallDir).Path
    if ($resolvedSourceDir -ine $resolvedInstallDir) {
        Write-Log "アプリケーションファイルをコピーしています..."
        Copy-Item -Path (Join-Path $AppSourceDir "*") -Destination $InstallDir -Recurse -Force
    }
    else {
        Write-Log "配置元とインストール先が同一のため、コピーをスキップします。"
    }

    # -------------------------------
    # 6. 設定書き込み・依存解決
    # -------------------------------
    $settingsScriptPath = Join-Path $PSScriptRoot "set-radicorder-storage-settings.ps1"
    if (-not (Test-Path -Path $settingsScriptPath)) { Exit-WithCode -Code $ExitCodeInstall -Message "設定スクリプトが見つかりません。" }
    $settingsPath = Join-Path $InstallDir "radicorder.settings.json"

    try {
        & $settingsScriptPath -RecordDir $RecordDir -TempDir $TempDir -SettingsPath $settingsPath -CreateDirectories
    }
    catch {
        Exit-WithCode -Code $ExitCodeInstall -Message ("radicorder.settings.json の更新に失敗しました: {0}" -f $_.Exception.Message)
    }

    Ensure-Ffmpeg -TargetInstallDir $InstallDir -SettingsPath $settingsPath -WingetEnabled $UseWingetForFfmpeg -DirectDownloadEnabled $UseDirectDownloadForFfmpeg -ChocolateyEnabled $UseChocolateyForFfmpeg -ExplicitFfmpegPath $FfmpegPath
    Ensure-RadiCorderFirewallRule -Port $HttpPort

    # -------------------------------
    # 7. タスク登録と起動確認
    # -------------------------------
    if ($null -ne $existingTask -and $Force) {
        Unregister-ScheduledTask -TaskName $ServiceName -Confirm:$false -ErrorAction SilentlyContinue
    }

    $entryPath = Resolve-AppEntryPath -BaseDir $InstallDir
    $executePath = ""
    $argumentText = ""
    if ([System.IO.Path]::GetExtension($entryPath).Equals(".exe", [System.StringComparison]::OrdinalIgnoreCase)) {
        $executePath = $entryPath
        $argumentText = "--urls http://0.0.0.0:$HttpPort"
    }
    else {
        $dotnetPath = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
        if ([string]::IsNullOrWhiteSpace($dotnetPath)) {
            Exit-WithCode -Code $ExitCodeTaskRegister -Message "dll 起動用の dotnet コマンドが見つかりません。"
        }
        $executePath = $dotnetPath
        $argumentText = "`"$entryPath`" --urls http://0.0.0.0:$HttpPort"
    }

    Write-Log "タスクスケジューラへ登録しています..."
    Register-RadiCorderScheduledTask -TaskName $ServiceName -ExecutePath $executePath -ArgumentText $argumentText -WorkingDir $InstallDir -Description $ServiceDescription

    if (-not $SkipServiceStart) {
        Write-Log "タスクを起動しています..."
        Start-RadiCorderTaskAndCheckHealth -TaskName $ServiceName -Port $HttpPort
    }

    # -------------------------------
    # 8. 完了案内
    # -------------------------------
    $lanIp = Get-PrimaryIPv4Address
    Write-Log "インストールが完了しました。"
    Write-Log ("アクセスURL(ローカル): http://127.0.0.1:{0}" -f $HttpPort)
    if (-not [string]::IsNullOrWhiteSpace($lanIp)) {
        Write-Log ("アクセスURL(LAN): http://{0}:{1}" -f $lanIp, $HttpPort)
    }
    Write-Log ("インストールログ: {0}" -f $script:LogFilePath)
    exit $ExitCodeSuccess
}
catch {
    Exit-WithCode -Code $ExitCodeUnexpected -Message ("想定外エラー: {0}" -f $_.Exception.Message)
}


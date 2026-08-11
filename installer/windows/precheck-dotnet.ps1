param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimeConfigPath,
    [string]$LogPath = "$env:TEMP\radicorder-dotnet-precheck.log"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Log {
    param([Parameter(Mandatory = $true)][string]$Message)
    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Message
    Write-Host $line
    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        Add-Content -Path $LogPath -Value $line -Encoding UTF8
    }
}

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

function Get-RequiredFrameworks {
    param([Parameter(Mandatory = $true)][string]$ConfigPath)

    $requirements = @{}

    function Add-Requirement {
        param(
            [hashtable]$Map,
            [string]$Name,
            [string]$Version
        )

        if ([string]::IsNullOrWhiteSpace($Name) -or [string]::IsNullOrWhiteSpace($Version)) { return }

        $major = 0
        try {
            $major = [int]($Version.Split('.')[0])
        }
        catch {
            $major = 0
        }
        if ($major -le 0) { return }

        if ($Map.ContainsKey($Name)) {
            if ($major -gt $Map[$Name].Major) {
                $Map[$Name] = [pscustomobject]@{ Name = $Name; Major = $major }
            }
            return
        }

        $Map[$Name] = [pscustomobject]@{ Name = $Name; Major = $major }
    }

    if (Test-Path -Path $ConfigPath) {
        try {
            $json = Get-Content -Raw -Path $ConfigPath | ConvertFrom-Json
            $runtimeOptions = $json.runtimeOptions

            $frameworkProp = $runtimeOptions.PSObject.Properties["framework"]
            if ($null -ne $frameworkProp -and $null -ne $frameworkProp.Value) {
                Add-Requirement -Map $requirements -Name $frameworkProp.Value.name -Version $frameworkProp.Value.version
            }

            $frameworksProp = $runtimeOptions.PSObject.Properties["frameworks"]
            if ($null -ne $frameworksProp -and $null -ne $frameworksProp.Value) {
                foreach ($fw in @($frameworksProp.Value)) {
                    Add-Requirement -Map $requirements -Name $fw.name -Version $fw.version
                }
            }
        }
        catch {
            Write-Log ("runtimeconfig の解析に失敗したため、既定要件で判定します: {0}" -f $_.Exception.Message)
        }
    }

    if ($requirements.Count -eq 0) {
        Add-Requirement -Map $requirements -Name "Microsoft.NETCore.App" -Version "10.0.0"
        Add-Requirement -Map $requirements -Name "Microsoft.AspNetCore.App" -Version "10.0.0"
    }

    return @($requirements.Values)
}

function Test-FrameworkInstalled {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][int]$Major,
        [Parameter(Mandatory = $true)][string[]]$InstalledLines
    )

    $escaped = [Regex]::Escape($Name)
    foreach ($line in $InstalledLines) {
        if ($line -match ("^{0}\s+{1}\." -f $escaped, $Major)) {
            return $true
        }
    }
    return $false
}

try {
    if (Test-Path -Path $LogPath) {
        Remove-Item -Path $LogPath -Force -ErrorAction SilentlyContinue
    }

    Write-Log "DotNet 事前チェックを開始します。"
    Write-Log ("runtimeconfig: {0}" -f $RuntimeConfigPath)

    $requiredFrameworks = @(Get-RequiredFrameworks -ConfigPath $RuntimeConfigPath)
    foreach ($fw in $requiredFrameworks) {
        Write-Log ("必要ランタイム: {0} {1}.x" -f $fw.Name, $fw.Major)
    }

    $dotnetPath = Get-DotNetHostPath
    if ([string]::IsNullOrWhiteSpace($dotnetPath)) {
        Write-Log ".NET ホスト (dotnet.exe) が見つかりません。"
        exit 10
    }

    Write-Log ("dotnet 検出パス: {0}" -f $dotnetPath)
    $runtimes = @(& $dotnetPath --list-runtimes 2>$null)
    if ($runtimes.Count -eq 0) {
        Write-Log "dotnet --list-runtimes の結果が0件です。"
    }

    $missing = @()
    foreach ($fw in $requiredFrameworks) {
        if (-not (Test-FrameworkInstalled -Name $fw.Name -Major $fw.Major -InstalledLines $runtimes)) {
            $missing += ("{0} {1}.x" -f $fw.Name, $fw.Major)
        }
    }

    if ($missing.Count -gt 0) {
        Write-Log ("不足ランタイム: {0}" -f ($missing -join ", "))
        exit 12
    }

    Write-Log "DotNet 事前チェックは成功しました。"
    exit 0
}
catch {
    Write-Log ("事前チェックで例外が発生しました: {0}" -f $_.Exception.Message)
    exit 99
}


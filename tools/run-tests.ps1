[CmdletBinding()]
param(
    [string] $GodotBin = $env:GODOT_BIN,

    [string[]] $TestRoots = @(),

    [ValidateRange(1, 3600)]
    [int] $TestTimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$ProjectFile = Join-Path $ProjectRoot "9Die.csproj"
$SolutionFile = Join-Path $ProjectRoot "9Die.sln"
$AnsiEscapePattern = [regex]::Escape([string][char]27) + "\[[0-?]*[ -/]*[@-~]"

# Keep this list empty by default. If an engine error is temporarily unavoidable, add the
# narrowest possible regular expression here together with an issue/reference explaining it.
$AllowedEngineErrorPatterns = @(
)

function ConvertTo-NativeCommandLineArgument {
    param([AllowEmptyString()][string] $Argument)

    if ($Argument.Length -gt 0 -and $Argument -notmatch '[\s"]') {
        return $Argument
    }

    # ProcessStartInfo.ArgumentList is unavailable in Windows PowerShell 5.1. Apply the Windows
    # CommandLineToArgvW escaping rules explicitly so paths with spaces remain one argument.
    $escaped = [Text.StringBuilder]::new()
    [void] $escaped.Append('"')
    $backslashCount = 0

    foreach ($character in $Argument.ToCharArray()) {
        if ($character -eq [char] 92) {
            $backslashCount++
            continue
        }

        $slashesToWrite = if ($character -eq [char] 34) {
            ($backslashCount * 2) + 1
        }
        else {
            $backslashCount
        }
        for ($index = 0; $index -lt $slashesToWrite; $index++) {
            [void] $escaped.Append([char] 92)
        }

        [void] $escaped.Append($character)
        $backslashCount = 0
    }

    # A closing quote would consume trailing backslashes unless each one is doubled.
    for ($index = 0; $index -lt ($backslashCount * 2); $index++) {
        [void] $escaped.Append([char] 92)
    }
    [void] $escaped.Append('"')
    return $escaped.ToString()
}

function Invoke-NativeCaptured {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [string[]] $ArgumentList = @(),

        [ValidateRange(0, 86400)]
        [int] $TimeoutSeconds = 0
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Arguments = @(
        $ArgumentList | ForEach-Object { ConvertTo-NativeCommandLineArgument -Argument $_ }
    ) -join " "

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $timedOut = $false
    $exitCode = 1

    try {
        if (-not $process.Start()) {
            throw "Nao foi possivel iniciar '$FilePath'."
        }

        # Read both streams concurrently so a verbose child process cannot block on a full pipe.
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()

        if ($TimeoutSeconds -gt 0) {
            $finished = $process.WaitForExit($TimeoutSeconds * 1000)
            if (-not $finished) {
                $timedOut = $true
                try {
                    $process.Kill($true)
                }
                catch {
                    $process.Kill()
                }
            }
        }

        $process.WaitForExit()
        $standardOutput = $standardOutputTask.GetAwaiter().GetResult()
        $standardError = $standardErrorTask.GetAwaiter().GetResult()
        $combinedOutput = @($standardOutput, $standardError) -join [Environment]::NewLine
        $output = @(
            [regex]::Split($combinedOutput, '\r?\n') |
                Where-Object { $_.Length -gt 0 }
        )
        $exitCode = if ($timedOut) { 124 } else { $process.ExitCode }
    }
    finally {
        $process.Dispose()
    }

    return [PSCustomObject]@{
        ExitCode = [int] $exitCode
        Output = $output
        TimedOut = $timedOut
    }
}

function Get-RequiredGodotVersion {
    $projectContents = [IO.File]::ReadAllText($ProjectFile)
    $sdkMatch = [regex]::Match(
        $projectContents,
        'Godot\.NET\.Sdk/(?<version>\d+\.\d+\.\d+)'
    )

    if (-not $sdkMatch.Success) {
        throw "Nao foi possivel determinar a versao do Godot.NET.Sdk em $ProjectFile."
    }

    return $sdkMatch.Groups["version"].Value
}

function Resolve-ExecutableReference {
    param([Parameter(Mandatory = $true)][string] $Reference)

    $trimmed = $Reference.Trim().Trim('"')
    if (Test-Path -LiteralPath $trimmed -PathType Leaf) {
        return (Resolve-Path -LiteralPath $trimmed).Path
    }

    $command = Get-Command $trimmed -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $command) {
        return $command.Source
    }

    return $null
}

function Test-GodotCandidate {
    param(
        [Parameter(Mandatory = $true)][string] $CandidatePath,
        [Parameter(Mandatory = $true)][string] $RequiredVersion
    )

    if (-not (Test-Path -LiteralPath $CandidatePath -PathType Leaf)) {
        return $null
    }

    $resolvedPath = (Resolve-Path -LiteralPath $CandidatePath).Path
    $versionResult = Invoke-NativeCaptured -FilePath $resolvedPath -ArgumentList @("--version")
    $versionText = ($versionResult.Output -join " ").Trim()

    if ($versionResult.ExitCode -ne 0) {
        Write-Verbose "Ignorando '$resolvedPath': --version retornou $($versionResult.ExitCode)."
        return $null
    }

    if ($versionText -notmatch '(?i)\.mono(?:\.|$)') {
        Write-Verbose "Ignorando '$resolvedPath': a build nao e Mono/C# ($versionText)."
        return $null
    }

    if ($versionText -notmatch ('^' + [regex]::Escape($RequiredVersion) + '(?:\.|$)')) {
        Write-Verbose "Ignorando '$resolvedPath': esperado $RequiredVersion, encontrado $versionText."
        return $null
    }

    return [PSCustomObject]@{
        Path = $resolvedPath
        Version = $versionText
    }
}

function Find-Godot {
    param(
        [string] $RequestedPath,
        [Parameter(Mandatory = $true)][string] $RequiredVersion
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $explicitPath = Resolve-ExecutableReference -Reference $RequestedPath
        if ($null -eq $explicitPath) {
            throw "GODOT_BIN aponta para '$RequestedPath', mas o executavel nao foi encontrado."
        }

        $explicitCandidate = Test-GodotCandidate `
            -CandidatePath $explicitPath `
            -RequiredVersion $RequiredVersion
        if ($null -eq $explicitCandidate) {
            throw "GODOT_BIN precisa apontar para o Godot $RequiredVersion Mono/C#."
        }

        return $explicitCandidate
    }

    $candidatePaths = @()
    foreach ($commandName in @("godot", "godot4")) {
        $command = Get-Command $commandName -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $command) {
            $candidatePaths += $command.Source
        }
    }

    $searchRoots = @((Split-Path -Parent $ProjectRoot))
    $documents = [Environment]::GetFolderPath("MyDocuments")
    if (-not [string]::IsNullOrWhiteSpace($documents)) {
        $searchRoots += (Join-Path $documents "Godot")
    }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $searchRoots += (Join-Path $env:LOCALAPPDATA "Programs\Godot")
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $searchRoots += (Join-Path $env:ProgramFiles "Godot")
    }

    foreach ($searchRoot in @($searchRoots | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $searchRoot -PathType Container)) {
            continue
        }

        $candidatePaths += @(
            Get-ChildItem -LiteralPath $searchRoot -File -Filter "Godot*.exe" `
                -ErrorAction SilentlyContinue |
                Sort-Object @{ Expression = { if ($_.Name -like "*_console.exe") { 0 } else { 1 } } }, Name |
                ForEach-Object { $_.FullName }
        )

        foreach ($engineDirectory in @(
            Get-ChildItem -LiteralPath $searchRoot -Directory -Filter "Godot*" `
                -ErrorAction SilentlyContinue
        )) {
            $candidatePaths += @(
                Get-ChildItem -LiteralPath $engineDirectory.FullName -File -Filter "Godot*.exe" `
                    -ErrorAction SilentlyContinue |
                    Sort-Object @{ Expression = { if ($_.Name -like "*_console.exe") { 0 } else { 1 } } }, Name |
                    ForEach-Object { $_.FullName }
            )
        }
    }

    foreach ($candidatePath in @($candidatePaths | Select-Object -Unique)) {
        $candidate = Test-GodotCandidate `
            -CandidatePath $candidatePath `
            -RequiredVersion $RequiredVersion
        if ($null -ne $candidate) {
            return $candidate
        }
    }

    throw "Godot $RequiredVersion Mono/C# nao encontrado. Defina GODOT_BIN com o caminho do executavel."
}

function Get-UnallowlistedEngineFailures {
    param([string[]] $Output)

    $failures = @()
    foreach ($line in $Output) {
        $plainLine = [regex]::Replace($line, $AnsiEscapePattern, "")
        $isEngineError = $plainLine -match '(?i)(?:^|\s)(?:SCRIPT ERROR|ERROR):'
        $isLeakWarning = $plainLine -match '(?i)(?:^|\s)WARNING:.*\bleak(?:ed|s|ing)?\b'
        if (-not $isEngineError -and -not $isLeakWarning) {
            continue
        }

        $isAllowed = $false
        foreach ($allowedPattern in $AllowedEngineErrorPatterns) {
            if ($plainLine -match $allowedPattern) {
                $isAllowed = $true
                break
            }
        }

        if (-not $isAllowed) {
            $failures += $plainLine
        }
    }

    return $failures
}

function Get-TestSummary {
    param([string[]] $Output)

    $summaryCount = 0
    $passed = 0
    $failed = 0

    foreach ($line in $Output) {
        $plainLine = [regex]::Replace($line, $AnsiEscapePattern, "")
        $summaryMatch = [regex]::Match(
            $plainLine,
            '===\s*(?<passed>\d+)\s+passaram,\s*(?<failed>\d+)\s+falharam\s*===',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase
        )
        if (-not $summaryMatch.Success) {
            continue
        }

        $summaryCount++
        $passed += [int] $summaryMatch.Groups["passed"].Value
        $failed += [int] $summaryMatch.Groups["failed"].Value
    }

    return [PSCustomObject]@{
        SummaryCount = $summaryCount
        Passed = $passed
        Failed = $failed
    }
}

function Write-FailureOutput {
    param([string[]] $Output)

    foreach ($line in $Output) {
        Write-Host "    $line"
    }
}

$processExitCode = 1
Push-Location $ProjectRoot
try {
    $requiredGodotVersion = Get-RequiredGodotVersion
    $godot = Find-Godot -RequestedPath $GodotBin -RequiredVersion $requiredGodotVersion

    $dotnetCommand = Get-Command "dotnet" -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $dotnetCommand) {
        throw "dotnet nao foi encontrado no PATH."
    }

    $sdkResult = Invoke-NativeCaptured -FilePath $dotnetCommand.Source -ArgumentList @("--version")
    if ($sdkResult.ExitCode -ne 0) {
        throw "dotnet --version falhou com exit code $($sdkResult.ExitCode)."
    }

    Write-Host "Godot: $($godot.Version)" -ForegroundColor Cyan
    Write-Host "Godot bin: $($godot.Path)"
    Write-Host "dotnet SDK: $(($sdkResult.Output -join ' ').Trim())"

    Write-Host "`n[1/3] Compilando a solucao..." -ForegroundColor Cyan
    $buildResult = Invoke-NativeCaptured `
        -FilePath $dotnetCommand.Source `
        -ArgumentList @("build", $SolutionFile, "--configuration", "Debug", "--verbosity", "minimal")
    foreach ($line in $buildResult.Output) {
        Write-Host $line
    }
    if ($buildResult.ExitCode -ne 0) {
        throw "Build falhou com exit code $($buildResult.ExitCode)."
    }

    Write-Host "`n[2/3] Validando/importando recursos do projeto..." -ForegroundColor Cyan
    $importResult = Invoke-NativeCaptured `
        -FilePath $godot.Path `
        -ArgumentList @("--headless", "--path", $ProjectRoot, "--import")
    $importFailures = @(
        Get-UnallowlistedEngineFailures -Output $importResult.Output
    )
    foreach ($line in $importResult.Output) {
        Write-Verbose $line
        if ($line -match '(?i)(?:^|\s)(?:Godot Engine|WARNING:|SCRIPT ERROR:|ERROR:)') {
            Write-Host $line
        }
    }
    if ($importResult.ExitCode -ne 0 -or $importFailures.Count -gt 0) {
        Write-FailureOutput -Output $importResult.Output
        throw "Importacao falhou: exit $($importResult.ExitCode), $($importFailures.Count) erro(s) de engine."
    }

    # Never recurse into editor caches, vendored code, assets or local Codex worktrees.
    # The latter may contain full copies of this repository and would run every test twice.
    $excludedTopLevelDirectories = @(
        ".git",
        ".godot",
        ".claude",
        ".github",
        "addons",
        "Assets",
        "docs",
        "tools"
    )
    $resolvedTestRoots = @()

    if (@($TestRoots).Count -gt 0) {
        foreach ($requestedRoot in $TestRoots) {
            if ([string]::IsNullOrWhiteSpace($requestedRoot)) {
                throw "TestRoots contem uma raiz vazia."
            }

            $candidateRoot = if ([IO.Path]::IsPathRooted($requestedRoot)) {
                [IO.Path]::GetFullPath($requestedRoot)
            }
            else {
                [IO.Path]::GetFullPath((Join-Path $ProjectRoot $requestedRoot))
            }

            $projectPrefix = $ProjectRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
            $isProjectRoot = $candidateRoot.Equals(
                $ProjectRoot,
                [StringComparison]::OrdinalIgnoreCase
            )
            if (-not $isProjectRoot -and -not $candidateRoot.StartsWith(
                $projectPrefix,
                [StringComparison]::OrdinalIgnoreCase
            )) {
                throw "A raiz de testes '$requestedRoot' esta fora do projeto."
            }

            if (-not (Test-Path -LiteralPath $candidateRoot -PathType Container)) {
                throw "A raiz de testes '$requestedRoot' nao existe."
            }

            $relativeRoot = if ($isProjectRoot) {
                "."
            }
            else {
                $candidateRoot.Substring($ProjectRoot.Length + 1)
            }
            $topLevelName = ($relativeRoot -split '[\\/]')[0]
            if ($excludedTopLevelDirectories -contains $topLevelName) {
                throw "A raiz '$requestedRoot' esta explicitamente excluida da descoberta de testes."
            }

            $resolvedTestRoots += $candidateRoot
        }
    }
    else {
        $resolvedTestRoots = @(
            Get-ChildItem -LiteralPath $ProjectRoot -Directory |
                Where-Object { $excludedTopLevelDirectories -notcontains $_.Name } |
                ForEach-Object { $_.FullName }
        )
    }

    $testCandidates = @(
        Get-ChildItem -LiteralPath $ProjectRoot -File -Filter "*Test.tscn"
    )
    foreach ($testRoot in @($resolvedTestRoots | Select-Object -Unique)) {
        $testCandidates += @(
            Get-ChildItem -LiteralPath $testRoot -File -Filter "*Test.tscn" -Recurse
        )
    }

    $testFiles = @(
        $testCandidates |
            Where-Object {
                $relativeCandidate = $_.FullName.Substring($ProjectRoot.Length + 1)
                $topLevelName = ($relativeCandidate -split '[\\/]')[0]
                $excludedTopLevelDirectories -notcontains $topLevelName
            } |
            Sort-Object FullName -Unique
    )
    if ($testFiles.Count -eq 0) {
        throw "Nenhuma cena automatizada *Test.tscn foi encontrada."
    }

    $displayRoots = @($resolvedTestRoots | ForEach-Object {
        if ($_.Equals($ProjectRoot, [StringComparison]::OrdinalIgnoreCase)) {
            "."
        }
        else {
            $_.Substring($ProjectRoot.Length + 1).Replace('\', '/')
        }
    })
    Write-Host "Raizes de teste: $($displayRoots -join ', ')"
    Write-Host "`n[3/3] Executando $($testFiles.Count) cenas de teste..." -ForegroundColor Cyan
    $testResults = @()

    foreach ($testFile in $testFiles) {
        $relativePath = $testFile.FullName.Substring($ProjectRoot.Length + 1).Replace('\', '/')
        $scenePath = "res://$relativePath"
        $runResult = Invoke-NativeCaptured `
            -FilePath $godot.Path `
            -ArgumentList @("--headless", "--path", $ProjectRoot, "--scene", $scenePath) `
            -TimeoutSeconds $TestTimeoutSeconds

        $summary = Get-TestSummary -Output $runResult.Output
        $engineFailures = @(Get-UnallowlistedEngineFailures -Output $runResult.Output)
        $passedRun = $runResult.ExitCode -eq 0 `
            -and -not $runResult.TimedOut `
            -and $summary.SummaryCount -eq 1 `
            -and $summary.Failed -eq 0 `
            -and $engineFailures.Count -eq 0

        $status = if ($passedRun) { "PASS" } else { "FAIL" }
        $color = if ($passedRun) { "Green" } else { "Red" }
        Write-Host (
            "[{0}] {1} - {2} passaram, {3} falharam, exit {4}, engine errors {5}, timeout {6}" -f
                $status,
                $relativePath,
                $summary.Passed,
                $summary.Failed,
                $runResult.ExitCode,
                $engineFailures.Count,
                $runResult.TimedOut
        ) -ForegroundColor $color

        foreach ($line in $runResult.Output) {
            Write-Verbose $line
        }
        if (-not $passedRun) {
            Write-FailureOutput -Output $runResult.Output
        }

        $testResults += [PSCustomObject]@{
            Scene = $relativePath
            Status = $status
            Passed = $summary.Passed
            Failed = $summary.Failed
            ExitCode = $runResult.ExitCode
            EngineErrors = $engineFailures.Count
            Summaries = $summary.SummaryCount
            TimedOut = $runResult.TimedOut
        }
    }

    $totalPassed = ($testResults | Measure-Object -Property Passed -Sum).Sum
    $totalFailed = ($testResults | Measure-Object -Property Failed -Sum).Sum
    $failedRuns = @($testResults | Where-Object { $_.Status -eq "FAIL" })

    Write-Host "`nResumo" -ForegroundColor Cyan
    $testResults | Format-Table Scene, Status, Passed, Failed, ExitCode, EngineErrors, TimedOut -AutoSize
    Write-Host (
        "Cenas: {0} | Checks: {1} passaram, {2} falharam | Cenas com falha: {3}" -f
            $testResults.Count,
            $totalPassed,
            $totalFailed,
            $failedRuns.Count
    )

    if ($failedRuns.Count -gt 0 -or $totalFailed -gt 0) {
        $processExitCode = 1
        Write-Host "Suite reprovada." -ForegroundColor Red
    }
    else {
        $processExitCode = 0
        Write-Host "Suite aprovada." -ForegroundColor Green
    }
}
catch {
    Write-Host "`nERRO DO RUNNER: $($_.Exception.Message)" -ForegroundColor Red
    $processExitCode = 1
}
finally {
    Pop-Location
}

exit $processExitCode

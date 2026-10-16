<#
  Names each failed test of a run as a GitHub annotation, so a failure can be read on the run's page (and through the
  public API) without signing in to see the log: reads the .trx files `dotnet test --logger trx` wrote under <folder>
  and prints, for each failed test, an ::error:: line titled with its name and holding the first lines of its message,
  stack and output. A run that was aborted (a crashed or hung test host) says so, with the test it was on when the hang
  collector (--blame-hang-timeout) names one. GitHub shows ten errors a step, so past nine failures the rest are named in
  one more. Outside GitHub Actions it prints the same as plain text.
    pwsh -File tools\test-failures.ps1 [-Results TestResults] [-Lines 12]
#>
param(
    [string]$Results = 'TestResults',
    [int]$Lines = 12
)
$ErrorActionPreference = 'Stop'
$github = $env:GITHUB_ACTIONS -eq 'true'

# Workflow commands: a message escapes %, CR and LF; a property (the title) also : and ,.
function Escape([string]$text) { $text.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A') }
function EscapeProperty([string]$text) { (Escape $text).Replace(':', '%3A').Replace(',', '%2C') }
function Report([string]$title, [string]$message) {
    if ($github) { Write-Output "::error title=$(EscapeProperty $title)::$(Escape $message)" }
    else { Write-Output "FAILED $title`n$message`n" }
}
function FirstLines([string]$text, [int]$count) {
    if (-not $text) { return '' }
    $all = $text.Trim() -split "`r?`n"
    $kept = ($all | Select-Object -First $count) -join "`n"
    if ($all.Count -gt $count) { $kept += "`n... ($($all.Count - $count) more lines)" }
    $kept
}

$files = @(if (Test-Path $Results) { Get-ChildItem $Results -Recurse -Filter *.trx })
if ($files.Count -eq 0) {
    Report 'No test results' "No .trx file under $Results`: the tests didn't run (see the build's errors), or the test host died before writing one."
    exit 0
}

$failed = New-Object System.Collections.Generic.List[object]
$aborted = 0
foreach ($file in $files) {
    [xml]$trx = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    $ns = New-Object System.Xml.XmlNamespaceManager $trx.NameTable
    $ns.AddNamespace('t', $trx.DocumentElement.NamespaceURI)
    foreach ($result in $trx.SelectNodes('//t:UnitTestResult[@outcome="Failed"]', $ns)) {
        $parts = @()
        $message = $result.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $ns)
        $stack = $result.SelectSingleNode('t:Output/t:ErrorInfo/t:StackTrace', $ns)
        $stdout = $result.SelectSingleNode('t:Output/t:StdOut', $ns)
        if ($message) { $parts += FirstLines $message.InnerText $Lines }
        if ($stack) { $parts += FirstLines $stack.InnerText $Lines }
        if ($stdout) { $parts += "Output:`n" + (FirstLines $stdout.InnerText $Lines) }
        $failed.Add([pscustomobject]@{ Name = $result.testName; Text = "$($file.Name): " + $(if ($parts) { $parts -join "`n" } else { 'failed, with no message' }) })
    }

    # An aborted run (the host crashed, or the hang collector stopped it) may name no failed test at all: its run errors say why.
    $errors = @($trx.SelectNodes('//t:ResultSummary/t:RunInfos/t:RunInfo[@outcome="Error"]/t:Text', $ns) | ForEach-Object { $_.InnerText.Trim() })
    if ($errors.Count -gt 0) {
        # The hang collector's sequence lists the tests the host started, the unfinished one last (or marked).
        $running = @(Get-ChildItem (Split-Path $file.FullName) -Recurse -Filter 'Sequence_*.xml' -ErrorAction SilentlyContinue | ForEach-Object {
            $tests = @(([xml](Get-Content -LiteralPath $_.FullName -Raw)).SelectNodes('//*[@Name]'))
            $open = @($tests | Where-Object { $_.GetAttribute('Completed') -eq 'False' })
            if ($open.Count -gt 0) { $open | ForEach-Object { $_.GetAttribute('Name') } } elseif ($tests.Count -gt 0) { $tests[-1].GetAttribute('Name') }
        })
        $text = FirstLines ($errors -join "`n") $Lines
        if ($running.Count -gt 0) { $text += "`nRunning when it stopped: $($running -join ', ')" }
        Report "$($file.Name): the test run was aborted" $text
        $aborted++
    }
}

$room = [Math]::Max(1, 10 - $aborted)
$shown = if ($failed.Count -gt $room) { $room - 1 } else { $failed.Count }
foreach ($test in $failed | Select-Object -First $shown) {
    Report $test.Name $test.Text
}
if ($failed.Count -gt $shown) {
    Report "$($failed.Count - $shown) more failed tests" (($failed | Select-Object -Skip $shown | ForEach-Object { $_.Name }) -join "`n")
}
if (-not $github) { Write-Output "$($failed.Count) failed tests in $($files.Count) result files." }
exit 0

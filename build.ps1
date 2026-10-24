param([switch]$Test, [switch]$Release)
$ErrorActionPreference = 'Stop'
$taskFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $taskFramework 'csc.exe'))) {
    $taskFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$taskCompiler = Join-Path $taskFramework 'csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw '.NET Framework C# compiler not found.' }
$taskSource = Join-Path $PSScriptRoot 'src'
$taskOutput = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskExe = Join-Path $taskOutput 'AI대전기록.exe'
& $taskCompiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/out:$taskExe" "/resource:$taskSource\MainWindow.xaml,MainWindow.xaml" "/resource:$taskSource\TextImport.xaml,TextImport.xaml" "/resource:$taskSource\AllRecords.xaml,AllRecords.xaml" /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.Xaml.dll "/reference:$taskFramework\WPF\WindowsBase.dll" "/reference:$taskFramework\WPF\PresentationCore.dll" "/reference:$taskFramework\WPF\PresentationFramework.dll" (Join-Path $taskSource 'AiMatchLog.cs') (Join-Path $taskSource 'ExtraWindows.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($Test) {
    $taskTestDir = Join-Path $PSScriptRoot '.test'
    New-Item -ItemType Directory -Force -Path $taskTestDir | Out-Null
    $taskReport = Join-Path $taskTestDir 'results.txt'
    $taskArguments = '--self-test "' + $taskReport + '"'
    $taskProcess = Start-Process -FilePath $taskExe -ArgumentList $taskArguments -Wait -PassThru -WindowStyle Hidden
    if (Test-Path -LiteralPath $taskReport) { Get-Content -LiteralPath $taskReport -Encoding UTF8 }
    if ($taskProcess.ExitCode -ne 0) { throw 'Self-tests failed.' }
}
if ($Release) {
    $taskDist = Join-Path $PSScriptRoot 'dist'
    New-Item -ItemType Directory -Force -Path $taskDist | Out-Null
    Copy-Item -LiteralPath $taskExe -Destination (Join-Path $taskDist 'AI대전기록.exe') -Force
}
Write-Output $taskExe

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Install the Windows .NET Framework 4.x compiler before building.'
}
$buildDirectory = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
$compilerArguments = @(
    '/nologo', '/optimize+', '/target:winexe', '/platform:x64', '/codepage:65001',
    ('/out:' + (Join-Path $buildDirectory 'SpeedFan Presets.exe')),
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'src\Presets.manifest')),
    ('/win32icon:' + (Join-Path $PSScriptRoot 'assets\fan.ico')),
    '/reference:System.Windows.Forms.dll,System.Drawing.dll,Microsoft.CSharp.dll',
    (Join-Path $PSScriptRoot 'src\FanPresets.cs'),
    (Join-Path $PSScriptRoot 'src\SpeedFanControl.cs')
)
& $compiler @compilerArguments
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
Write-Output (Join-Path $buildDirectory 'SpeedFan Presets.exe')

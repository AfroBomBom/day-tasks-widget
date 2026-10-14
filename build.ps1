param([switch]$DesktopTests)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Не найден компилятор .NET Framework.' }
$app = Join-Path $PSScriptRoot 'app\DayTasks.exe'
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'app') -Force | Out-Null
$compileArgs = @(
    '/nologo', '/target:winexe', '/optimize+',
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'src\DayTasks.manifest')),
    ('/win32icon:' + (Join-Path $PSScriptRoot 'assets\daytasks.ico')),
    ('/out:' + $app),
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
    (Join-Path $PSScriptRoot 'src\DayTasks.cs'),
    (Join-Path $PSScriptRoot 'src\DesktopShadow.cs'),
    (Join-Path $PSScriptRoot 'src\WallpaperBackdrop.cs')
)
& $compiler @compileArgs
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась.' }
$checks = Join-Path $PSScriptRoot 'work\checks'
$check = Start-Process -FilePath $app -ArgumentList '--self-test', ('"' + $checks + '"') -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath (Join-Path $checks 'test-result.txt')
if ($check.ExitCode -ne 0) { throw 'Проверки не пройдены.' }
if ($DesktopTests) {
    $check = Start-Process -FilePath $app -ArgumentList '--effects-test', ('"' + $checks + '"') -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath (Join-Path $checks 'effects-result.txt')
    if ($check.ExitCode -ne 0) { throw 'Проверка прозрачности и тени не пройдена.' }
}
Write-Output ('Готово: ' + $app)


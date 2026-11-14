<#
.SYNOPSIS
    Publishes SQL Server Manager as a Windows desktop app and installs it for the current user.

.DESCRIPTION
    Builds a self-contained win-x64 app (no .NET install needed on the target PC), copies it to
    %LOCALAPPDATA%\Programs\SQL Server Manager and adds Desktop and Start menu shortcuts.
    The app opens in its own window and stops when the window is closed.

.EXAMPLE
    .\publish-desktop.ps1
    .\publish-desktop.ps1 -Output D:\Apps\SqlManager -NoShortcuts
    .\publish-desktop.ps1 -FrameworkDependent     # smaller, needs .NET 10 Desktop + ASP.NET Core runtimes
#>
param(
    [string]$Output = (Join-Path $env:LOCALAPPDATA 'Programs\SQL Server Manager'),
    [switch]$FrameworkDependent,
    [switch]$NoShortcuts
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'MigrateSQLServerDatabases.csproj'
$exeName = 'MigrateSQLServerDatabases.exe'
$appName = 'SQL Server Manager'

# A running copy locks its files
Get-Process -Name 'MigrateSQLServerDatabases' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($Output, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object { Write-Host "Closing the running app (PID $($_.Id))..."; $_ | Stop-Process -Force; Start-Sleep -Milliseconds 800 }

Write-Host "Publishing to $Output ..."
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
& dotnet publish $project -c Release -r win-x64 --self-contained $selfContained -p:DesktopBuild=true -o $Output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)." }

$exe = Join-Path $Output $exeName
if (-not (Test-Path $exe)) { throw "$exe was not produced." }

if (-not $NoShortcuts) {
    $shell = New-Object -ComObject WScript.Shell
    $places = @(
        [Environment]::GetFolderPath('Desktop'),
        (Join-Path ([Environment]::GetFolderPath('Programs')) $appName)
    )
    foreach ($place in $places) {
        New-Item -ItemType Directory -Force -Path $place | Out-Null
        $link = $shell.CreateShortcut((Join-Path $place "$appName.lnk"))
        $link.TargetPath = $exe
        $link.WorkingDirectory = $Output
        $link.IconLocation = "$exe,0"
        $link.Description = 'Manage SQL Server databases and migrate data'
        $link.Save()
        Write-Host "Shortcut: $(Join-Path $place "$appName.lnk")"
    }
}

Write-Host ''
Write-Host "Done. Start it from the '$appName' shortcut or run:"
Write-Host "  `"$exe`""

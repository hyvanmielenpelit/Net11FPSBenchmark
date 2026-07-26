<# 
.SYNOPSIS
    Switches the benchmark project between .NET 10 and .NET 11.
    
.DESCRIPTION
    Swaps global.json, cleans bin/obj, and restores packages.
    The csproj auto-detects the SDK version.
    
.EXAMPLE
    .\switch-dotnet.ps1 10
    .\switch-dotnet.ps1 11
#>
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("10", "11")]
    [string]$Version
)

$projectDir = $PSScriptRoot

# Clean build artifacts from the previous .NET version
foreach ($dir in @("bin", "obj")) {
    $path = Join-Path $projectDir $dir
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
        Write-Host "Deleted $dir/" -ForegroundColor DarkGray
    }
}

Copy-Item (Join-Path $projectDir "global.net${Version}.json") (Join-Path $projectDir "global.json") -Force

Write-Host "Switched to .NET $Version" -ForegroundColor $(if ($Version -eq "10") { "Yellow" } else { "Cyan" })
Write-Host "SDK: $(dotnet --version)"
Write-Host ""

# Restore packages for the new SDK
Write-Host "Restoring packages..." -ForegroundColor Gray
dotnet restore

Write-Host ""
Write-Host "Done! Run 'dotnet build -c Release' to build." -ForegroundColor Green

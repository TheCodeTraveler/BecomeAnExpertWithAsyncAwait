$ErrorActionPreference = 'Stop'

$searchFolder = Get-Location

Write-Host "Searching for solution files (*.slnx) in '$searchFolder'..." -ForegroundColor Cyan
$slnxFiles = Get-ChildItem -Path $searchFolder -Recurse -Filter *.slnx -ErrorAction SilentlyContinue | Sort-Object FullName

if ($slnxFiles.Count -eq 0) {
    Write-Host "No solution files (*.slnx) found in '$searchFolder'." -ForegroundColor Yellow
    exit 0
}

$hasError = $false

foreach ($slnxFile in $slnxFiles) {
    Write-Host "Formatting solution: $($slnxFile.FullName)" -ForegroundColor Cyan
    & dotnet format whitespace $slnxFile.FullName

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Format failed for: $($slnxFile.FullName)" -ForegroundColor Red
        $hasError = $true
        continue
    }

    Write-Host "Building solution: $($slnxFile.FullName)" -ForegroundColor Cyan
    # --no-incremental forces a recompile so analyzer warnings are always reported, even when nothing changed
    $buildOutput = & dotnet build $slnxFile.FullName -c Release --no-incremental
    $buildExitCode = $LASTEXITCODE
    $buildOutput | ForEach-Object { Write-Host $_ }

    # StyleCop element ordering (SA1201, SA1202, SA1204, SA1214, SA1215) is a warning for attendees but a failure here.
    # See "C# element ordering" in .github/copilot-instructions.md.
    $orderingWarnings = $buildOutput | Select-String -Pattern 'warning SA1(201|202|204|214|215)' | ForEach-Object { $_.Line.Trim() } | Sort-Object -Unique

    if ($buildExitCode -ne 0) {
        Write-Host "Build failed for: $($slnxFile.FullName)" -ForegroundColor Red
        $hasError = $true
    }
    elseif ($orderingWarnings) {
        Write-Host "Build reported StyleCop element ordering warnings for: $($slnxFile.FullName)" -ForegroundColor Red
        $orderingWarnings | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        $hasError = $true
    }
    else {
        Write-Host "Build succeeded for: $($slnxFile.FullName)" -ForegroundColor Green
    }
}

if ($hasError) {
    Write-Host "One or more solutions failed to format or build." -ForegroundColor Red
    exit 1
}

Write-Host "Format and build process completed." -ForegroundColor Cyan
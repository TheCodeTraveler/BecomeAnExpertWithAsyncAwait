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
    & dotnet build $slnxFile.FullName -c Release

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Build succeeded for: $($slnxFile.FullName)" -ForegroundColor Green
    }
    else {
        Write-Host "Build failed for: $($slnxFile.FullName)" -ForegroundColor Red
        $hasError = $true
    }
}

if ($hasError) {
    Write-Host "One or more solutions failed to format or build." -ForegroundColor Red
    exit 1
}

Write-Host "Format and build process completed." -ForegroundColor Cyan
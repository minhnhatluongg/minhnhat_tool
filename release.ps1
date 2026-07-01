# =============================================================
#  ĐÓNG GÓI BẢN PHÁT HÀNH (auto-update qua Velopack)
#  Chạy:  .\release.ps1 -Version 1.0.0
#  Lần sau ra bản mới: tăng số, ví dụ  .\release.ps1 -Version 1.0.1
# =============================================================
param(
    [Parameter(Mandatory = $true)] [string]$Version
)

$ErrorActionPreference = "Stop"
$Root      = $PSScriptRoot
$Proj      = Join-Path $Root "minhnhat_tool\minhnhat_tool.csproj"
$Icon      = Join-Path $Root "minhnhat_tool\app.ico"
$PublishDir= Join-Path $Root "publish"
$Releases  = Join-Path $Root "releases"          # GIỮ NGUYÊN thư mục này để tạo bản delta cho các lần sau
$PackId    = "QuanLyHoaDon"                       # id cố định, KHÔNG đổi giữa các phiên bản
$MainExe   = "minhnhat_tool.exe"                  # = AssemblyName.exe

Write-Host "==> 1/3  dotnet publish (self-contained, win-x64)..." -ForegroundColor Cyan
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
dotnet publish $Proj -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish lỗi" }

Write-Host "==> 2/3  vpk pack (tạo Setup.exe + nupkg + releases.*.json)..." -ForegroundColor Cyan
vpk pack --packId $PackId --packVersion $Version --packDir $PublishDir `
    --mainExe $MainExe --packTitle "Quản lý hóa đơn" --icon $Icon --outputDir $Releases
if ($LASTEXITCODE -ne 0) { throw "vpk pack lỗi" }

Write-Host "==> 3/3  XONG. Nội dung cần upload lên IIS nằm ở:" -ForegroundColor Green
Write-Host "    $Releases" -ForegroundColor Yellow
Write-Host ""
Write-Host "LẦN ĐẦU: đưa cho khách file  $Releases\$PackId-win-Setup.exe  (cài 1 lần)." -ForegroundColor Green
Write-Host "MỖI LẦN RA BẢN MỚI: copy TOÀN BỘ thư mục 'releases' đè lên thư mục 'app' trên IIS." -ForegroundColor Green

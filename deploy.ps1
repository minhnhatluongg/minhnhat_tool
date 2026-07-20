# =============================================================
#  ĐỒNG BỘ releases -> server (chỉ đẩy file mới/khác, đè file cũ)
#  Chạy:  .\deploy.ps1
#  Yêu cầu: đã cài WinSCP (có WinSCP.com). Server chạy FileZilla FTP hoặc SFTP.
# =============================================================

# ---- CẤU HÌNH (điền 1 lần) ----
$Local    = "D:\Code\minhnhat_tool\releases"
$Remote   = "/app_releases"                       # đường dẫn thư mục trên server (theo gốc FTP)
$WinScp   = "C:\Program Files (x86)\WinSCP\WinSCP.com"

# Cách 1 (khuyên dùng): tên session đã lưu trong WinSCP GUI -> an toàn, không lộ mật khẩu
$Session  = ""                                    # ví dụ: "decapcha-ftp"  (để trống nếu dùng Cách 2)

# Cách 2: điền trực tiếp (nếu chưa lưu session)
$Protocol = "ftp"                                 # "ftp" (FileZilla) hoặc "sftp"
$HostName = "10.10.212.1"                          # hoặc decapcha.win-tech.vn
$User     = "ftpuser"
$Pass     = "ftppassword"
# --------------------------------

if (-not (Test-Path $WinScp)) { Write-Host "Không thấy WinSCP.com tại $WinScp — sửa lại đường dẫn." -ForegroundColor Red; exit 1 }

$open = if ($Session) { $Session } else { "$Protocol`://$User`:$Pass@$HostName" }

Write-Host "==> Đồng bộ $Local  ->  $Remote (chỉ file mới/khác)..." -ForegroundColor Cyan
& $WinScp /log=NUL /command `
    "open $open" `
    "option batch abort" `
    "option confirm off" `
    "synchronize remote -criteria=either ""$Local"" ""$Remote""" `
    "exit"

if ($LASTEXITCODE -eq 0) { Write-Host "==> XONG. Đã đẩy các file thay đổi lên server." -ForegroundColor Green }
else { Write-Host "==> Có lỗi (exit $LASTEXITCODE). Kiểm tra host/user/pass hoặc đường dẫn Remote." -ForegroundColor Red }

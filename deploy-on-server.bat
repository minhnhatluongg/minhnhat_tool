@echo off
chcp 65001 >nul
REM =============================================================
REM  CHAY FILE NAY *TREN SERVER* (trong phien Remote Desktop).
REM  No keo file releases tu MAY DEV (qua \\tsclient) -> app_releases,
REM  chi copy file MOI/KHAC (bo qua nupkg cu giong het). Khong can WinSCP.
REM
REM  Yeu cau: RDP da bat "Drives" (Local Resources) -> may dev hien la \\tsclient\D
REM =============================================================

REM --- Duong dan NGUON (may dev, qua RDP redirect). Doi chu o D neu khac. ---
set "SRC=\\tsclient\D\Code\minhnhat_tool\releases"

REM --- Duong dan DICH (tren server) ---
set "DST=D:\IIS WEB\decapcha.win-tech.vn\CaptchaService\app_releases"

echo ==^> Kiem tra nguon: %SRC%
if not exist "%SRC%\releases.win.json" (
    echo    KHONG thay nguon. Kiem tra: RDP da bat "Drives" chua? O dia co phai D khong?
    pause
    exit /b 1
)

echo ==^> Dong bo (chi file moi/khac)...
robocopy "%SRC%" "%DST%" /FFT /R:2 /W:2 /NP /NDL

echo.
if %ERRORLEVEL% GEQ 8 (
    echo ==^> LOI robocopy (code %ERRORLEVEL%).
) else (
    echo ==^> XONG. Da day cac file thay doi len app_releases.
)
pause

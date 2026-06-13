@echo off
setlocal
chcp 65001 >nul
set "SCRIPT_DIR=%~dp0"
set "EXE=%SCRIPT_DIR%BlueShift.exe"

if not exist "%EXE%" (
    echo.
    echo [エラー] BlueShift.exe が見つかりません。
    echo 探した場所: %SCRIPT_DIR%
    echo.
    echo この bat は exe と同じフォルダに置いて実行してください。
    echo 例: bin\x64\Debug\net8.0-windows10.0.19041.0\ResetBlueShiftGamma.bat
    echo.
    pause
    exit /b 1
)

echo BlueShift の画面補正を標準に戻しています...
"%EXE%" --reset-gamma
if errorlevel 1 (
    echo 失敗しました。
    pause
    exit /b 1
)

echo 完了しました。画面の色味が通常に戻っているか確認してください。
pause

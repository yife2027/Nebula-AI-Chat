@echo off
chcp 65001 >nul
setlocal

REM ===== 配置区域：根据你的实际路径修改 =====
set PROJECT_DIR=D:\dsp-mod\MyDysonMod
set GAME_DIR=D:\mystream\dsp[steam]\steamApps\common\Dyson Sphere Program
set PLUGIN_NAME=Nebula-AI.dll
REM =========================================

set OUTPUT_DLL=%PROJECT_DIR%\bin\Release\%PLUGIN_NAME%
set TARGET_DIR=%GAME_DIR%\BepInEx\plugins\nebula-AI

echo "[0/4] 正在清理旧文件..."
if exist "%PROJECT_DIR%\bin" rd /S /Q "%PROJECT_DIR%\bin"
if exist "%PROJECT_DIR%\obj" rd /S /Q "%PROJECT_DIR%\obj"

if exist "%TARGET_DIR%\%PLUGIN_NAME%" (
    del /F /Q "%TARGET_DIR%\%PLUGIN_NAME%"
    if exist "%TARGET_DIR%\%PLUGIN_NAME%" (
        echo "[错误] 无法删除旧 DLL，请确认游戏已完全关闭。"
        pause
        exit /b 1
    )
)

echo.
echo "[1/4] 正在编译项目..."
cd /d "%PROJECT_DIR%"
dotnet build -c Release
if %errorlevel% neq 0 (
    echo "[错误] 编译失败，请检查上方错误信息。"
    pause
    exit /b 1
)

if not exist "%OUTPUT_DLL%" (
    echo "[错误] 编译后找不到产物: %OUTPUT_DLL%"
    pause
    exit /b 1
)

echo.
echo "[2/4] 正在复制 DLL 到游戏插件目录..."
if not exist "%TARGET_DIR%" (
    mkdir "%TARGET_DIR%"
)

copy /Y "%OUTPUT_DLL%" "%TARGET_DIR%\" >nul
if not exist "%TARGET_DIR%\%PLUGIN_NAME%" (
    echo "[错误] 复制后找不到目标文件。"
    pause
    exit /b 1
)

echo.
echo "[3/4] 编译完成！"
echo "DLL 已复制到: %TARGET_DIR%\%PLUGIN_NAME%"
echo "启动游戏即可加载插件。"
echo.
pause
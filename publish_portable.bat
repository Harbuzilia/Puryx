@echo off
echo ======================================================================
echo   Publishing Single-File Portable Release: SmartCleaner
echo ======================================================================
echo.

cd /d "%~dp0"

echo [1/3] Cleaning previous release folder...
if exist "release\portable" rd /s /q "release\portable"

echo [2/3] Publishing self-contained single-file binary (.NET 8 LTS)...
dotnet publish SmartCleaner.App\SmartCleaner.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o release\portable

echo [3/3] Copying community plugins...
if exist "SmartCleaner.Data\Plugins" (
    xcopy /s /y /i "SmartCleaner.Data\Plugins" "release\portable\Plugins"
)

echo.
echo ======================================================================
echo   Build Successful! Portable executable ready at:
echo   release\portable\SmartCleaner.App.exe
echo ======================================================================

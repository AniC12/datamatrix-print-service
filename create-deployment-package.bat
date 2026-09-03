@echo off
REM Code Print Manager - Create Deployment Package
REM This script builds and packages the application for deployment

echo ================================================================================
echo  Code Print Manager - Deployment Package Creator
echo ================================================================================
echo.

REM Check if we're in the right directory
if not exist "application\CodePrintManager.sln" (
    echo ERROR: Please run this script from the project root directory
    echo Current directory: %CD%
    pause
    exit /b 1
)

echo [1/4] Building self-contained package...
cd application
dotnet publish src/Hosts/CodePrintManager.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=true -o ../publish/CodePrintManager
if errorlevel 1 (
    echo ERROR: Build failed
    cd ..
    pause
    exit /b 1
)
cd ..

echo.
echo [2/4] Copying README...
copy /Y DEPLOYMENT_README.txt publish\CodePrintManager\README.txt >nul
if errorlevel 1 (
    echo WARNING: Could not copy README
)

echo.
echo [3/4] Creating ZIP archive...
powershell -Command "Compress-Archive -Path publish\CodePrintManager -DestinationPath publish\CodePrintManager-v1.0.zip -Force"
if errorlevel 1 (
    echo ERROR: Failed to create ZIP archive
    pause
    exit /b 1
)

echo.
echo [4/4] Calculating package size...
for %%A in (publish\CodePrintManager-v1.0.zip) do set size=%%~zA
set /a sizeMB=%size% / 1048576

echo.
echo ================================================================================
echo  DEPLOYMENT PACKAGE CREATED SUCCESSFULLY
echo ================================================================================
echo.
echo Package location: %CD%\publish\CodePrintManager-v1.0.zip
echo Package size: %sizeMB% MB
echo.
echo Folder location: %CD%\publish\CodePrintManager\
echo.
echo You can now:
echo  1. Copy the ZIP file to another PC and extract it
echo  2. Copy the entire CodePrintManager folder to another PC
echo  3. Run CodePrintManager.Desktop.exe to start the application
echo.
echo See DEPLOYMENT_GUIDE.md for detailed instructions.
echo ================================================================================
echo.
pause

@echo off
cd /d "%~dp0"
dotnet publish -p:PublishProfile=SingleFile
if errorlevel 1 (
    echo.
    pause
    exit /b 1
)
echo.
echo output: bin\Publish\

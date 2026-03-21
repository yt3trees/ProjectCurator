@echo off
cd /d "%~dp0"
dotnet publish -p:PublishProfile=SingleFile
echo.
echo output: bin\Publish\
pause

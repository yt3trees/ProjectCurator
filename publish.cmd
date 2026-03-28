@echo off
cd /d "%~dp0"
echo Building ProjectCurator (Windows x64)...
dotnet publish ProjectCurator.Desktop/ProjectCurator.Desktop.csproj -r win-x64 -c Release -o bin/Publish --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
echo.
echo Output: bin\Publish\

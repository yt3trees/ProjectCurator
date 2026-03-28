#!/bin/bash
set -e
cd "$(dirname "$0")"

echo "Building ProjectCurator.Desktop (macOS arm64)..."
dotnet publish ProjectCurator.Desktop/ProjectCurator.Desktop.csproj \
  -r osx-arm64 -c Release \
  -o bin/Publish/osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
echo "Output: bin/Publish/osx-arm64/"

echo ""
echo "Building ProjectCurator.Desktop (macOS x64)..."
dotnet publish ProjectCurator.Desktop/ProjectCurator.Desktop.csproj \
  -r osx-x64 -c Release \
  -o bin/Publish/osx-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
echo "Output: bin/Publish/osx-x64/"

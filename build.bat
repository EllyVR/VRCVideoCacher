@echo off
if exist Build rmdir /s /q Build
mkdir Build

echo Building for Windows x64...
dotnet publish VRCVideoCacher.UI/VRCVideoCacher.UI.csproj -c Release -o Build/win-x64

echo Building for Linux x64...
dotnet publish VRCVideoCacher.UI/VRCVideoCacher.UI.csproj -c Release -r linux-x64 -o Build/linux-x64

echo Done!

@echo off
if exist Build rmdir /s /q Build
mkdir Build

echo Building for Github Windows x64...
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c Release -r win-x64 -o ./Build/win-x64

echo Building for Github Linux x64...
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c Release -r linux-x64 -o ./Build/linux-x64

echo Building for Steam Windows x64...
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c SteamRelease -r win-x64 -o ./Build/Steam/win-x64

echo Building for Steam Linux x64...
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c SteamRelease -r linux-x64 -o ./Build/Steam/linux-x64

echo Done!

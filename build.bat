@echo off
if exist Build rmdir /s /q Build

mkdir Build 
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o Build/

dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o Build/

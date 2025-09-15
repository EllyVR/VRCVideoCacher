#!/bin/sh
dotnet publish -r win-x64 -c Release && cp bin/Release/net9.0/win-x64/publish/yt-dlp-stub.exe ../VRCVideoCacher/

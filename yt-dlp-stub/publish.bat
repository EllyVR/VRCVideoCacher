dotnet publish -r win-x64 -c Release
dotnet publish -r linux-x64 -c Release
xcopy bin\Release\net10.0\win-x64\publish\yt-dlp-stub.exe ..\VRCVideoCacher\ /Y
xcopy bin\Release\net10.0\linux-x64\publish\yt-dlp-stub ..\VRCVideoCacher\yt-dlp-stub_linux /Y
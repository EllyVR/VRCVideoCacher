@echo off
REM SABR feature-branch test build.
REM
REM Same as the normal Release build, but the version carries a "-sabr" suffix and the self-updater is
REM disabled (SABRRELEASE). Both matter: "-sabr" is a SemVer prerelease, which ranks BELOW the plain
REM release, so a build that could self-update would immediately replace itself with mainline.
REM
REM Output goes to Build/Sabr/ so it can't be confused with a real release.

if exist Build\Sabr rmdir /s /q Build\Sabr

echo Building SABR test build, Windows x64...
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c SabrRelease -r win-x64 -o ./Build/Sabr/win-x64

echo Building SABR test build, Linux x64...
dotnet publish VRCVideoCacher/VRCVideoCacher.csproj -c SabrRelease -r linux-x64 -o ./Build/Sabr/linux-x64

echo Done - Build/Sabr/

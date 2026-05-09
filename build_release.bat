@echo off
setlocal

pushd "%~dp0JumpzysVortex.App"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false -p:DebugType=none -p:DebugSymbols=false -o ..\publish
popd

echo.
echo Release build written to:
echo %~dp0publish
pause

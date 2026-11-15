@echo off
rem Builds VoxDeck.exe (single file, no .NET needed on the target PC). Requires the .NET 8 SDK or newer.
dotnet publish src\VoxDeck.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
echo.
echo Done: publish\VoxDeck.exe

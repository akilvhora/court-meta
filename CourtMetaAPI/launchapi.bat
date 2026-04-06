@echo off
echo Bundling Chrome extension into wwwroot...
dotnet msbuild -t:BundleExtension -nologo -v:minimal
echo.
echo Starting Court Meta API on http://localhost:5000 ...
dotnet run --project CourtMetaAPI.csproj
pause

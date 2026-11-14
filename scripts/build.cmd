@echo off
REM Build helper for ISBNQuery (Windows). Requires .NET 10 SDK.
REM Usage: build.cmd test|publish|selftest|bench|clean
setlocal
cd /d "%~dp0.."
if not defined DOTNET set "DOTNET=dotnet.exe"

if /i "%~1"=="test"     goto test
if /i "%~1"=="publish"  goto publish
if /i "%~1"=="selftest" goto selftest
if /i "%~1"=="bench"    goto bench
if /i "%~1"=="clean"    goto clean
echo Usage: build.cmd test^|publish^|selftest^|bench^|clean
exit /b 2

:test
"%DOTNET%" test ISBNQuery.sln -c Release --nologo -p:NuGetAudit=false
exit /b %errorlevel%

:publish
"%DOTNET%" publish src\ISBNQuery -c Release -p:NuGetAudit=false -o dist --nologo
exit /b %errorlevel%

:selftest
if not exist dist\ISBNQuery.exe echo dist\ISBNQuery.exe not found - run publish first & exit /b 1
dist\ISBNQuery.exe --selftest
exit /b %errorlevel%

:bench
"%DOTNET%" run -c Release --project bench\IsbnBench --nologo
exit /b %errorlevel%

:clean
git clean -xdq -e dist
exit /b %errorlevel%

@echo off
REM ---------------------------------------------------------------------------
REM Runs ArchSql against a live SQL Server using the connection string in
REM conn.txt (same folder as this script). Output goes to .\site-live and opens
REM in your browser.
REM
REM Usage:  run-connect.cmd            (uses conn.txt and site-live)
REM         run-connect.cmd myconn.txt (uses a different connection file)
REM         run-connect.cmd myconn.txt myoutdir
REM
REM conn.txt must contain ONLY the connection string, e.g.:
REM   Server=HOST;Database=DB;User ID=ro_user;Password=...;TrustServerCertificate=True
REM Use a read-only login. Delete conn.txt when finished (it holds a password).
REM ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0"

set "CONN=%~1"
if "%CONN%"=="" set "CONN=conn.txt"
set "OUT=%~2"

if not exist "%CONN%" (
  echo error: connection file "%CONN%" not found. Create it next to this script.
  exit /b 2
)

echo Building...
dotnet build src\ArchSql\ArchSql.csproj -v quiet
if errorlevel 1 (
  echo error: build failed.
  exit /b 1
)

if "%OUT%"=="" (  
  echo Connecting and generating site...
  dotnet run --project src\ArchSql --no-build -- connect --conn-file "%CONN%"
)
else (
  echo Connecting and generating site into "%OUT%"...
  dotnet run --project src\ArchSql --no-build -- connect --conn-file "%CONN%" --out "%OUT%"
)
set "RC=%errorlevel%"

if "%RC%"=="0" (
  echo Done. Site written to "%OUT%\index.html".
) else (
  echo archsql exited with code %RC%.
)
endlocal & exit /b %RC%

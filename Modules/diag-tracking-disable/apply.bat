@echo off
setlocal

set "CAPTURE=%~1"
set "MARKERDIR=%LOCALAPPDATA%\PSuite\markers"
set "MARKER=%MARKERDIR%\diag-tracking-disable.marker"

if not exist "%MARKERDIR%" mkdir "%MARKERDIR%" >nul 2>&1

set "EXISTED=false"
if exist "%MARKER%" set "EXISTED=true"

> "%CAPTURE%" echo {"markerExistedBefore":%EXISTED%}
> "%MARKER%" echo %DATE% %TIME%

echo {"success":true,"requiresRestart":false,"details":"Marker written."}

endlocal

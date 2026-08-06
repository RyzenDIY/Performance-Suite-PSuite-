@echo off
setlocal

set "CAPTURE=%~1"
set "MARKER=%LOCALAPPDATA%\PSuite\markers\diag-tracking-disable.marker"

set "EXISTED=true"
if exist "%CAPTURE%" (
    findstr /c:"\"markerExistedBefore\":false" "%CAPTURE%" >nul 2>&1 && set "EXISTED=false"
)

if /i "%EXISTED%"=="false" (
    if exist "%MARKER%" del /f /q "%MARKER%" >nul 2>&1
)

echo {"success":true,"details":"Marker removed (or left as originally captured)."}

endlocal

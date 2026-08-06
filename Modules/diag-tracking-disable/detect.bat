@echo off
setlocal

set "MARKER=%LOCALAPPDATA%\PSuite\markers\diag-tracking-disable.marker"

if exist "%MARKER%" (
    echo {"success":true,"state":"Applied","details":"Marker present."}
) else (
    echo {"success":true,"state":"NotApplied","details":"Marker absent."}
)

endlocal

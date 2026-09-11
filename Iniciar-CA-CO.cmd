@echo off
rem Lanzador de CA-CO sin consola visible.
rem Recomendado para desarrollar: F5 en Visual Studio. Este script es un atajo.
setlocal
cd /d "%~dp0"
start "" /min powershell -NoProfile -WindowStyle Hidden -Command "dotnet run --project \"%~dp0src\CA-CO.App\CaCo.App.csproj\" -c Debug -p:Platform=x64 --nologo -v q"
endlocal

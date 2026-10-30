@echo off
chcp 65001 >nul
title Relic Engram v2.077 — Johnny Silverhand Launcher
cd /d "%~dp0"

if exist "release\JohnnyOverlay.exe" (
    start "" "release\JohnnyOverlay.exe"
) else (
    cd frontend
    dotnet run
)


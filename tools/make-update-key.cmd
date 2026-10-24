@echo off
rem Ssok update signing key - double-click to run.
chcp 65001 >nul
dotnet run "%~dp0make-update-key.cs"
pause

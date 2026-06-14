@echo off
title ADN_pay - Tableau de bord (fermer pour arreter)
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0etat.ps1" -Watch

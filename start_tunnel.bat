@echo off
echo Tunnel ADN_pay - https://trycloudflare.com
echo.
cloudflared tunnel --url http://localhost:5163 --no-autoupdate
pause

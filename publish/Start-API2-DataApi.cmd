@echo off
rem API 2 (Mobile.DataApi) - runs on each client's server. It needs NO database login of its own: the phone sends the sealed config,
rem which holds the client's database login. The only setting is the key that opens that config.
rem ConfigKey must be the SAME text as Gateway__ConfigKey on API 1. Do not share this file once it is filled in.
set DataApi__ConfigKey=<the same text as Gateway__ConfigKey>
set DataApi__EnableSwagger=false
rem set DataApi__DbHost=localhost
cd /d "%~dp0DataApi"
.\Mobile.DataApi.exe --urls http://0.0.0.0:5091
pause

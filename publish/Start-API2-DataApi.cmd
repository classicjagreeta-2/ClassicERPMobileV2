@echo off
rem API 2 (Mobile.DataApi) - runs on each client's server. The phone sends only the access token from API 1 (company, unit, expiry);
rem the client's database login lives HERE, in this server's settings, and never goes to the phone.
rem ConfigKey must be the SAME text as Gateway__ConfigKey on API 1. Do not share this file once it is filled in.
set DataApi__ConfigKey=<the same text as Gateway__ConfigKey>
set DataApi__DbService=<Oracle service name of the client database>
set DataApi__DbUser=<client database user>
set DataApi__DbPassword=<client database password>
rem set DataApi__DbHost=localhost
rem set DataApi__DbPort=1521
set DataApi__EnableSwagger=false
cd /d "%~dp0DataApi"
.\Mobile.DataApi.exe --urls http://0.0.0.0:5091
pause

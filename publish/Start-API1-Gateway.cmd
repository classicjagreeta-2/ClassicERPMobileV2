@echo off
rem API 1 (Mobile.Gateway) - the central address the phone signs in at. Edit the values below, then double-click this file.
rem ConfigKey must be the SAME text on every API 2 (32+ characters). DbPasswordKey is shared only with MobileKeyManager ("Encryption key...").
rem Do not share this file once it is filled in.
set Gateway__ConnectionString=User Id=CLASSICNEW;Password=<password>;Data Source=//103.234.116.64:1521/CLASSICDEV
set Gateway__ConfigKey=<32+ random characters, the same on every API 2>
set Gateway__DbPasswordKey=<32+ random characters, the same as in MobileKeyManager>
set Gateway__EnableSwagger=false
rem set Gateway__Api2Scheme=https
cd /d "%~dp0Gateway"
.\Mobile.Gateway.exe --urls http://0.0.0.0:5090
pause

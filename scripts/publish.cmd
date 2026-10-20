@echo off
setlocal
cd /d "%~dp0.."

dotnet publish src\dbtool\dbtool.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o dist\win-x64
if errorlevel 1 (
  echo.
  echo Сборка завершилась с ошибкой.
  pause
  exit /b 1
)

echo.
echo Готово. Портативная сборка: dist\win-x64\dbtool.exe
echo Файл запускается без установки .NET: нужен только дистрибутив папки.
pause

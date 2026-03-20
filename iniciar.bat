@echo off
title Robot Jardinero - Iniciando...
color 0A

echo.
echo  =========================================
echo   ROBOT JARDINERO - Iniciando servicios
echo  =========================================
echo.

:: ── 1. Verificar que .NET esté instalado ─────────────────────────────────────
where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] .NET no encontrado. Instala .NET 10 desde https://dot.net
    pause
    exit /b 1
)

:: ── 2. Verificar que Flutter esté instalado ───────────────────────────────────
where flutter >nul 2>&1
if %errorlevel% neq 0 (
    set FLUTTER_CMD=C:\src\flutter\bin\flutter.bat
    if not exist "%FLUTTER_CMD%" (
        echo [ERROR] Flutter no encontrado.
        pause
        exit /b 1
    )
) else (
    set FLUTTER_CMD=flutter
)

:: ── 3. Ir al directorio del proyecto ─────────────────────────────────────────
cd /d "%~dp0"

:: ── 4. Instalar dependencias del backend (si no están) ───────────────────────
echo [1/4] Restaurando dependencias del backend...
cd backend
dotnet restore --nologo -q
cd ..

:: ── 5. Instalar dependencias de Flutter (si no están) ────────────────────────
echo [2/4] Restaurando dependencias de Flutter...
call %FLUTTER_CMD% pub get --suppress-analytics >nul 2>&1

:: ── 6. Iniciar el backend en ventana separada ─────────────────────────────────
echo [3/4] Iniciando backend ASP.NET Core en http://localhost:5000 ...
start "Backend - Robot Jardinero" cmd /k "cd /d "%~dp0backend" && dotnet run --urls http://localhost:5000"

:: ── 7. Esperar a que el backend esté listo ────────────────────────────────────
echo     Esperando que el backend responda...
:wait_loop
timeout /t 2 /nobreak >nul
curl -s http://localhost:5000/api/predict/health >nul 2>&1
if %errorlevel% neq 0 goto wait_loop
echo     Backend listo!

:: ── 8. Iniciar Flutter Windows ────────────────────────────────────────────────
echo [4/4] Iniciando aplicacion Flutter Windows...
echo.
start "Flutter - Robot Jardinero" cmd /k "cd /d "%~dp0" && %FLUTTER_CMD% run -d windows"

echo.
echo  =========================================
echo   Servicios iniciados correctamente
echo   Backend : http://localhost:5000
echo   Flutter : ventana Windows abierta
echo  =========================================
echo.
echo  Cierra esta ventana cuando quieras detener todo.
echo  (Las ventanas del backend y Flutter tienen sus propios controles)
echo.
pause

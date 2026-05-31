@echo off
setlocal EnableDelayedExpansion

REM ============================================================================
REM  Order Aggregator - one-shot dev launcher
REM ----------------------------------------------------------------------------
REM  Spustí kompletní lokální stack:
REM    1) Redis            (Docker, port 6379)  - agregační buffer
REM    2) Aspire Dashboard (Docker, UI 18888, OTLP/gRPC 4317) - observabilita
REM    3) OrderAggregator.Api (dotnet run, HTTPS) - samotná aplikace
REM
REM  Redis i Aspire běží jako pojmenované kontejnery na pozadí; API běží
REM  v popředí tohoto okna. Zavřením okna / Ctrl+C ukončíš jen API,
REM  kontejnery doběhnou dál (zastavíš je na konci skriptu nebo ručně).
REM ============================================================================

REM --- Konfigurace portů (uprav dle potřeby) ---------------------------------
set "HTTPS_PORT=7282"
set "HTTP_PORT=5047"
set "REDIS_PORT=6379"
set "ASPIRE_UI_PORT=18888"
set "ASPIRE_OTLP_PORT=4317"

REM Adresy, na kterých bude API naslouchat (přebíjí launchSettings.json).
set "ASPNETCORE_URLS=https://localhost:%HTTPS_PORT%;http://localhost:%HTTP_PORT%"
set "ASPNETCORE_ENVIRONMENT=Development"

set "API_PROJECT=%~dp0src\OrderAggregator.Api"

echo ============================================================
echo  Order Aggregator - dev stack
echo ============================================================

REM --- 0) Kontrola Dockeru ----------------------------------------------------
docker info >nul 2>&1
if errorlevel 1 (
    echo [CHYBA] Docker neni dostupny / nebezi. Spust Docker Desktop a zkus znovu.
    exit /b 1
)

REM --- 1) HTTPS dev certifikat ------------------------------------------------
REM  Kestrel potrebuje duveryhodny dev cert. Idempotentni - kdyz uz existuje
REM  a je duveryhodny, neudela nic. Pri prvnim behu Windows zobrazi dialog
REM  "duverovat certifikatu" -> potvrd ANO.
echo.
echo [1/4] Kontrola HTTPS dev certifikatu...
dotnet dev-certs https --check --trust >nul 2>&1
if errorlevel 1 (
    echo       Vytvarim a duveryhodnim dev certifikat ^(potvrd pripadny dialog^)...
    dotnet dev-certs https --trust
) else (
    echo       Dev certifikat je v poradku.
)

REM --- 2) Redis ---------------------------------------------------------------
echo.
echo [2/4] Redis ^(port %REDIS_PORT%^)...
docker ps --filter "name=^order-aggregator-redis$" --filter "status=running" --format "{{.Names}}" | findstr /i "order-aggregator-redis" >nul
if not errorlevel 1 (
    echo       Uz bezi.
) else (
    docker ps -a --filter "name=^order-aggregator-redis$" --format "{{.Names}}" | findstr /i "order-aggregator-redis" >nul
    if not errorlevel 1 (
        echo       Startuji existujici kontejner...
        docker start order-aggregator-redis >nul
    ) else (
        echo       Vytvarim novy kontejner...
        docker run -d --name order-aggregator-redis -p %REDIS_PORT%:6379 redis:7-alpine >nul
    )
)

REM --- 3) Aspire Dashboard ----------------------------------------------------
echo.
echo [3/4] Aspire Dashboard ^(UI %ASPIRE_UI_PORT%, OTLP %ASPIRE_OTLP_PORT%^)...
docker ps --filter "name=^order-aggregator-aspire$" --filter "status=running" --format "{{.Names}}" | findstr /i "order-aggregator-aspire" >nul
if not errorlevel 1 (
    echo       Uz bezi.
) else (
    docker ps -a --filter "name=^order-aggregator-aspire$" --format "{{.Names}}" | findstr /i "order-aggregator-aspire" >nul
    if not errorlevel 1 (
        echo       Startuji existujici kontejner...
        docker start order-aggregator-aspire >nul
    ) else (
        echo       Vytvarim novy kontejner...
        docker run -d --name order-aggregator-aspire ^
            -p %ASPIRE_UI_PORT%:18888 ^
            -p %ASPIRE_OTLP_PORT%:18889 ^
            -e DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true ^
            mcr.microsoft.com/dotnet/aspire-dashboard:latest >nul
    )
)

echo.
echo       Aspire Dashboard UI: http://localhost:%ASPIRE_UI_PORT%

REM --- 4) API -----------------------------------------------------------------
echo.
echo [4/4] Spoustim OrderAggregator.Api...
echo       HTTPS : https://localhost:%HTTPS_PORT%
echo       HTTP  : http://localhost:%HTTP_PORT%
echo       Swagger: https://localhost:%HTTPS_PORT%/swagger
echo       Scalar : https://localhost:%HTTPS_PORT%/scalar/v1
echo ============================================================
echo  (Ctrl+C ukonci API; Redis a Aspire kontejnery zustanou bezet)
echo ============================================================
echo.

REM Otevri prohlizec se Swaggerem, ale az kdyz API skutecne nabehne.
REM Bezi na pozadi (dotnet run nize blokuje toto okno): poolluje anonymni
REM /health/live (HTTP, bez TLS) a teprve po prvni uspesne odpovedi otevre
REM HTTPS Swagger. Necekame pevny cas - build/start muze trvat ruzne dlouho.
start "" /b powershell -NoProfile -Command ^
    "$ok=$false; $n=0; while(-not $ok -and $n -lt 120){ try{ Invoke-WebRequest -Uri 'http://localhost:%HTTP_PORT%/health/live' -UseBasicParsing -TimeoutSec 2 | Out-Null; $ok=$true }catch{ Start-Sleep -Seconds 1; $n++ } }; if($ok){ Start-Process 'https://localhost:%HTTPS_PORT%/swagger' } else { Write-Host 'API se nepodarilo dosahnout do 120s, browser neotevren.' }"

REM --no-launch-profile: ignoruj launchSettings.json, jinak by jeho applicationUrl
REM prebil nase ASPNETCORE_URLS a aplikace by nabehla jen na HTTP.
dotnet run --project "%API_PROJECT%" --no-launch-profile

REM --- Volitelny uklid kontejneru po ukonceni API ----------------------------
REM  Odkomentuj, pokud chces po zavreni API zastavit i kontejnery:
REM docker stop order-aggregator-redis order-aggregator-aspire >nul 2>&1

endlocal

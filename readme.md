# Order Aggregator

Vypracováný úkol - technické zadání/praktický úkol k pohovoru - Senior .NET vývojář

Vypracováno samostatně, ale s pomocí Clade Code, kterého jsem řídil a dával mantinely a cíle.

REST API v **.NET 10**, které přijímá objednávky/produkty, agreguje je podle `productId`
a **ne častěji než jednou za 20 vteřin** posílá agregovaný snapshot navazujícímu
systému.

Plné zadání viz [task-description.md](/task-description.md).

## Jak to funguje

1. `POST /api/orders` zvaliduje a uloží objednávky do **agregačního bufferu**.
2. Na pozadí běží smyčka, která **každých 20 s** buffer vyprázdní, sečte množství
   per produkt a pošle snapshot navazujícímu systému (zatím výpis do konzole).
3. Když odeslání selže, zkusí se to znovu (retry); po vyčerpání pokusů se dávka
   odloží na disk do **dead-letter** složky (nezacyklí se, data se neztratí).
4. Dead-letter složka se monitoruje a při objevení nového souboru se odeslání znovu zkusí.
   pokud se to povede, soubor se smaže; pokud ne, zůstane pro manuální zásah.


## Jak to spustit

### Pozor
``` 
Aktuálně je nastaveno tak, aby služba při odesílání agregovaných objednávek občas selhávala a vytvořila tak dead-letter, aby bylo vidět jak to funguje.  
V Docker Compose se dead-lettery ukládají do volume `deadletter-data` (uvnitř kontejneru `/data/deadletter`).  
Pokud chcete toto chování vypnout, nastavte v `docker-compose.yml` proměnnou `AggregatedOrderSender__Console__FailureProbability` na `0` (nebo `AggregatedOrderSender:Console:FailureProbability` v `appsettings.json`).  
```


### Rychle — všechno najednou (Docker Compose)


```bash
docker compose up --build
```

`docker compose up` (z kořene repa) postaví a nahodí kompletní lokální stack —
**API + Redis + Aspire Dashboard** — jedním příkazem. Vyžaduje běžící
**Docker Desktop** (případně Docker Engine + Compose v2).

Po nahození:

- **API**: `http://localhost:5047` (jen HTTP — TLS v produkci řeší reverse proxy)
- **Swagger**: `http://localhost:5047/swagger` · **Scalar**: `http://localhost:5047/scalar/v1`
- **Aspire Dashboard**: `http://localhost:18888`
- `/health/live`, `/health/ready` — health checky (bez autentizace)

Compose přebíjí konfiguraci přes env proměnné, aby kontejnery mířily na sebe po
interní síti místo na `localhost`: Redis (`OrderStore__Redis__ConnectionString=redis:6379`),
OTLP (`OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire:18889`) a dead-letter složku
(`DeadLetter__Directory=/data/deadletter`, namapovanou na volume kvůli přežití
restartu). Stack zastavíš `Ctrl+C`, případně `docker compose down`.

### Ručně (bez Dockeru pro API)

```bash
cd src
dotnet run --project OrderAggregator.Api
```

Po startu (port viz výpis v konzoli, typicky `https://localhost:7282`):
(pozor je zapnuto HTTPS a HSTS)

- otevři **`/swagger`** (nebo `/scalar/v1`) — interaktivní dokumentace API
- `/health/live`, `/health/ready` — health checky (bez autentizace)

> Výchozí konfigurace používá **Redis** jako úložiště. Buď spusť Redis
> (`docker run --rm -p 6379:6379 redis`), nebo v `appsettings.json` přepni
> `OrderStore:Kind` na `InMemory`.

## Volání API

Všechny `/api/*` endpointy vyžadují hlavičku **`X-Api-Key`** (dev klíče jsou
v `appsettings.json`):

```bash
curl -X POST http://localhost:5000/api/orders \
  -H "X-Api-Key: dev-key-please-rotate-in-production-0001" \
  -H "Content-Type: application/json" \
  -d '[{ "productId": "1", "quantity": 5 }, { "productId": "456", "quantity": 2 }]'
```

| Endpoint | Popis |
|---|---|
| `POST /api/orders` | přijme dávku objednávek do bufferu (vrací `202 Accepted`) |
| `GET /api/products` | seznam známých produktů |
| `GET /api/products/{id}` | jeden produkt |

Objednávka s neznámým `productId` odmítne **celou dávku** (`400`), ať ji klient
po opravě může bezpečně poslat znovu bez dvojího započtení.

## Konfigurace (`appsettings.json`)

| Sekce | K čemu |
|---|---|
| `OrderStore` | úložiště bufferu — `InMemory` nebo `Redis` |
| `Aggregation` | interval flushe, počet retry pokusů |
| `AggregatedOrderSender` | kam se posílá snapshot (zatím `Console`) |
| `DeadLetter` | složka pro neodeslané dávky |
| `Observability` | OpenTelemetry — viz níže |
| `ApiKey` | API klíče a název hlavičky |

## Observabilita (OpenTelemetry)

Aplikace exportuje **metriky, trasy i logy** přes OpenTelemetry. Nejjednodušší
způsob, jak si je prohlédnout, je **Aspire Dashboard** — `docker compose up` ho
nahodí jako součást stacku (UI na `http://localhost:18888`). Když pouštíš API
ručně mimo Compose, spustíš dashboard samostatně:

```bash
docker run --rm -it -p 18888:18888 -p 4317:18889 \
  -e DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true \
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

Pošli pár objednávek a otevři `http://localhost:18888`.
Uvidíš HTTP requesty, doménové metriky (počet objednávek, velikost a doba flushe,
úspěšná odeslání vs. dead-letter) a trasu flush cyklu.

Bez běžícího dashboardu se aplikace normálně spustí — telemetrie se jen nikam
neodešle. Vypnout úplně lze přes `Observability:Enabled: false`.

## Struktura

Řešení je rozdělené do projektů podle vrstev (doménové modely, kontrakty,
rozhraní, implementace služeb, API).

```
src/
├── OrderAggregator.Models/         doménové entity — leaf, nic neimportuje
├── OrderAggregator.Contracts/      wire DTO (OrderRequest, *Dto, *Response) — leaf, ready jako klientské SDK
├── OrderAggregator.Abstractions/   rozhraní (IOrderStore, IAggregatedOrderSender, IProductRepository, IDeadLetterWriter) — zná jen Models
├── OrderAggregator.Resources/      lokalizované texty (.resx) + source-generated ApiMessages — leaf
├── OrderAggregator.Shared/         sdílený kernel pod Services i Api — Configuration/ (options) + Const/ (LocalizationConstants)
├── OrderAggregator.Services/       implementace (stores, senders, flush service, dead-lettering, diagnostics)
├── OrderAggregator.Api/            ASP.NET Core minimal API, kompoziční kořen
├── OrderAggregator.Tests/          xUnit — Unit/ Integration/ Architecture/
└── OrderAggregator.LoadTests/      NBomber console runner (mimo `dotnet test`)
```


# Order Aggregator

Vypracováný úkol - technické zadání/praktický úkol k pohovoru - Senior .NET vývojář

Vypracováno samostatně, ale s pomocí Clade Code, kterého jsem řídil a dával mantinely a cíle.

REST API v **.NET 10**, které přijímá objednávky/produkty, agreguje je podle `productId`
a **ne častěji než jednou za 20 vteřin** posílá agregovaný snapshot navazujícímu
systému.

Plné zadání viz `task-description.md`.

## Jak to spustit

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

## Jak to funguje

1. `POST /api/orders` zvaliduje a uloží objednávky do **agregačního bufferu**.
2. Na pozadí běží smyčka, která **každých 20 s** buffer vyprázdní, sečte množství
   per produkt a pošle snapshot navazujícímu systému (zatím výpis do konzole).
3. Když odeslání selže, zkusí se to znovu (retry); po vyčerpání pokusů se dávka
   odloží na disk do **dead-letter** složky (nezacyklí se, data se neztratí).

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
způsob, jak si je prohlédnout, je **Aspire Dashboard** (jeden Docker kontejner):

```bash
docker run --rm -it -p 18888:18888 -p 4317:18889 \
  -e DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true \
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

Spusť dashboard, pak API, pošli pár objednávek a otevři `http://localhost:18888`.
Uvidíš HTTP requesty, doménové metriky (počet objednávek, velikost a doba flushe,
úspěšná odeslání vs. dead-letter) a trasu flush cyklu.

Bez běžícího dashboardu se aplikace normálně spustí — telemetrie se jen nikam
neodešle. Vypnout úplně lze přes `Observability:Enabled: false`.

## Testy

```bash
cd src
dotnet test
```

xUnit testy ve třech kategoriích: **Unit**, **Integration** (přes
`WebApplicationFactory`) a **Architecture** (ArchUnitNET hlídá závislosti mezi
projekty). 

## Struktura

Řešení je rozdělené do projektů podle vrstev (doménové modely, kontrakty,
rozhraní, implementace služeb, API). Detailní popis architektury a vzorů je
v `CLAUDE.md`.

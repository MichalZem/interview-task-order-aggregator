# Order Aggregator

Vypracováný úkol - technické zadání/praktický úkol k pohovoru - Senior .NET vývojář

Vypracováno samostatně, ale s pomocí Clade Code, kterého jsem řídil a dával mantinely a cíle.

REST API v **.NET 10**, které přijímá objednávky/produkty, agreguje je podle `productId`
a **ne častěji než jednou za 20 vteřin** posílá agregovaný snapshot navazujícímu
systému.

Plné zadání viz `task-description.md`.

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
Soubory s dead-lettery se ukládají do složky `C:/TEMP/DeadLetter` .  
Pokud chcete toto chování vypnout, nastavte `AggregatedOrderSender:Console:FailureProbability` v `appsettings.json` na 0.  
```


### Rychle — všechno najednou (Windows)


```bat
run-app.bat
```

`run-app.bat` (v kořeni repa) nahodí kompletní lokální stack v tomto pořadí:

1. ověří/založí **HTTPS dev certifikát** (`dotnet dev-certs https --trust` — při
   prvním běhu potvrď systémový dialog **Ano**),
2. spustí **Redis** v Dockeru (kontejner `order-aggregator-redis`, port 6379),
3. spustí **Aspire Dashboard** v Dockeru (UI `:18888`, OTLP `:4317`),
4. spustí **API** přes HTTPS a po jeho nahození **otevře prohlížeč** na Swaggeru.

Vyžaduje běžící **Docker Desktop**. Porty se dají změnit nahoře v souboru
(`HTTPS_PORT`, `HTTP_PORT`, …). Redis a Aspire běží jako pojmenované kontejnery
na pozadí; `Ctrl+C` ukončí jen API, kontejnery zůstanou běžet.

### Ručně

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


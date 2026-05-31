# Order Aggregator — kontext projektu

Take-home zadání ** Agregátor objednávek**. REST API v .NET 10, které
přijímá objednávky, ne častěji než jednou za 20 vteřin je agreguje podle
`productId` a posílá agregovaný snapshot navazujícímu systému.

Uživatelská dokumentace viz `readme.md`. Tento dokument je orientace pro
vývojářské/agentní práce nad kódem.

> **DŮLEŽITÉ — udržuj tento dokument živý.** Kdykoli přidáš novou vlastnost,
> vzor nebo závislost (endpoint, sender, store, konfig sekci, DI extension,
> architektonický invariant, konvenci…), **ve stejné změně aktualizuj i tento
> `CLAUDE.md`**. Nesmí zaostávat za kódem.

## Struktura řešení

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

- Solution: `src/OrderAggregator.slnx` (XML formát, bez klasického `.sln`).
- Build-wide konvence: `src/.editorconfig` + `src/Directory.Build.props`
  s `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — auto-import do
  každého projektu pod `src/`, warning = chyba buildu, nový projekt to zdědí.

## Build & test

```bash
cd src
dotnet build
dotnet test                                   # Redis testy se bez Dockeru Skipnou, s Dockerem běží
dotnet run --project OrderAggregator.Api

# Load test (vyžaduje běžící API; env LOADTEST_URL / LOADTEST_API_KEY / LOADTEST_RPS / LOADTEST_DURATION)
dotnet run -c Release --project OrderAggregator.LoadTests

# Observabilita — Aspire Dashboard (UI :18888, OTLP/gRPC :4317); spusť před API
docker run --rm -it -p 18888:18888 -p 4317:18889 \
  -e DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true \
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

Dev URL: typicky `http://localhost:5000` (port z `launchSettings.json`). Po startu:
- `/` → redirect na `/swagger`
- `/scalar/v1`, `/swagger`, `/openapi/v1.json` — UI + raw OpenAPI 3.1
- `/health/live`, `/health/ready` — anonymní liveness/readiness
- `POST /api/orders`, `GET /api/products`, `GET /api/products/{id}` — vyžadují `X-Api-Key`

## Organizace testů

- **Složky** v `OrderAggregator.Tests/` zrcadlí druh testu: `Unit/`,
  `Integration/` (WebApplicationFactory), `Architecture/` (ArchUnitNET).
  Namespace zůstává plochý `OrderAggregator.Tests`.
- **xUnit Traity** — každá třída nese `[Trait(TestCategories.Name, …)]`
  (`Unit`/`Integration`/`Architecture`, konstanty v `TestCategories.cs`).
  CLI: `dotnet test --filter Category=Unit`.
- Novou třídu zařaď do správné složky **i** označ Traitem.
- **Docker-dependent testy** (`RedisOrderStoreTests`): `[SkippableFact]`
  (`Xunit.SkippableFact`) + fixture `RedisFixture`, která staví kontejner
  **uvnitř `InitializeAsync`** (ne v ctor — `RedisBuilder` řeší Docker eagerly).
  Bez Dockeru → Skipped, s Dockerem → běží. Stejný vzor pro každou externí službu.

## Architektonické invarianty (vynucené ArchUnitNET testy)

V `OrderAggregator.Tests/Architecture/` — porušení sestřelí test:

- **Models**, **Contracts**, **Resources** jsou listové — neimportují žádný jiný projekt
- **Abstractions** smí znát jen Models
- **Shared** (konfigurace + konstanty) nesmí znát Services ani Api
- **Services** nesmí znát Api
- Typy v **Contracts** musí končit na `Dto` / `Request` / `Response`

Leaf pravidla používají helper `EveryProjectExcept(assembly, name)` (odvodí
zakázanou množinu jako „všechny ostatní projekty") → nový projekt je pokryt
automaticky. **Pozor:** bezparametrové `NotDependOnAny()` je tichý no-op
(nezávislost na prázdné množině → vždy zelené); doplněk se předává přes ten helper.

## Klíčové vzory

### Persistence + sender jsou pluggable
- `IOrderStore` — agregační buffer, dvě implementace v `Services/Stores/`,
  výběr přes `OrderStore:Kind` (`InMemory` | `Redis`):
  - **`InMemoryOrderStore`** (default) — jeden `Lock` + plain `Dictionary` swap
    (drain prohodí referenci za prázdný slovník). Load testem ověřeno jako
    rychlejší i jednodušší než `ReaderWriterLockSlim` + `ConcurrentDictionary`.
  - **`RedisOrderStore`** — buffer je Redis hash (`HINCRBY` per productId).
    `AddAsync` přes `MULTI`/`EXEC` transakci (all-or-nothing, retry nezapočte
    dvakrát). Drain: atomický `RENAME` stranou + `HGETALL` + `DEL`. Klíč je
    **per-instance** (`{HashKey}:{InstanceId}`) — víc instancí nad sdíleným
    Redisem si nelezou do dat, každá posílá vlastní snapshot. `InstanceId`
    z konfigurace, prázdný → `Environment.MachineName`. Přežije restart.
    `IConnectionMultiplexer` je singleton (registruje se jen pro `Redis`).
- `IAggregatedOrderSender` — odchozí sink v `Services/Senders/`, zatím jen
  `ConsoleAggregatedOrderSender`, výběr přes `AggregatedOrderSender:Kind`
  (`Console`). Switch připravený přidat Http/Kafka jako novou větev + třídu.
  - **Fault injection** pro test dead-letteru: `Console:FailureProbability`
    (`[0,1]`, default 0). V `appsettings.Development.json` je 0.5. V produkci 0.
- Switche v `OrderAggregatorServiceCollectionExtensions.cs` (`RegisterOrderStore`,
  `RegisterAggregatedOrderSender`); registrace přes `TryAddSingleton`, takže
  test může implementaci přebít bez `RemoveAll`.

### Retry → dead-letter na neúspěšný send
- `OrderAggregationFlushService` (hosted service) každých `FlushIntervalSeconds`
  (default 20, min 20) drainuje store a posílá. Send se zkusí **`SendMaxAttempts`×**
  (default 3) s lineárním backoffem (`SendRetryDelayMilliseconds × attempt`).
  Delay běží přes `TimeProvider` (testy ho nulují). `OperationCanceledException`
  se neretryuje (= shutdown).
- Když i poslední pokus selže, batch jde do **dead-letteru, ne zpět do storu**
  (`IDeadLetterWriter` v Abstractions, `FileDeadLetterWriter` v `Services/DeadLettering/`).
  Re-queue by zacyklil poison batch a nechal buffer růst; odložení na disk to
  rozsekne a pipeline jede dál.
- `FileDeadLetterWriter` píše JSON (`DeadLetter:Directory`, default `DeadLetter`)
  **atomicky** (temp + `File.Move`), camelCase = přesně to, co se nepodařilo
  odeslat → jde replaynout. Selže-li i zápis, `LogCritical` („data lost").
- **Graceful shutdown**: flush loop dělá při vypnutí finální drain + send.
  `Program.cs` má `HostOptions.ShutdownTimeout = 30 s`, ať to stihne doběhnout.

### Observabilita (OpenTelemetry)
- Celá kompozice v `Api/DependencyInjection/ObservabilityServiceCollectionExtensions.cs`
  (`AddAppObservability(configuration)`). Exportuje **traces + metrics + logs**
  přes **OTLP/gRPC** na lokální **Aspire Dashboard** (default `http://localhost:4317`).
- **Konfigurace** `ObservabilityOptions` (sekce `Observability`): `Enabled`
  (master switch), `ServiceName`, `OtlpEndpoint`. Env `OTEL_EXPORTER_OTLP_ENDPOINT`
  přebije endpoint. `Enabled=false` → žádný provider se neregistruje (instrumentace
  nic nestojí); integrační testy ho mají vyplé v test factory.
- **Auto-instrumentace zadarmo**: ASP.NET Core, HttpClient, Runtime (GC/paměť/thready).
- **Doménová instrumentace** v `Services/Diagnostics/` (Services nesmí znát Api,
  proto patří sem; Api ji jen konzumuje):
  - `OrderAggregationDiagnostics.ActivitySource` — flush běží na background timeru,
    auto-instrumentace ho nevidí; flush service kolem neprázdného flushe otevírá
    span `aggregation.flush` s tagy.
  - `OrderAggregationMetrics` (`IMeterFactory`) — registrovaná **bezpodmínečně**
    (`AddMetrics` + `TryAddSingleton`), bez subscribera je záznam no-op. Instrumenty:
    `orders.accepted`, `orders.rejected_batches`, `flush.batch_size`,
    `flush.duration` (ms), `flush.sent`, `flush.dead_lettered`.
  - Flush service bere `OrderAggregationMetrics?` jako **volitelný** ctor parametr
    (vzor jako `TimeProvider?`) → unit testy bez meteru, DI dodá reálnou instanci.
- **Nový signál:** metriku přidej do `OrderAggregationMetrics`, span přes
  `ActivitySource`. Nový `ActivitySource`/`Meter` zaregistruj v `AddAppObservability`
  přes `AddSource(...)` / `AddMeter(...)`.

### Health checks (liveness / readiness)
- Vše v `Api/Health/HealthChecksExtensions.cs`: `AddAppHealthChecks()` +
  `MapHealthChecks()`. `Program.cs` volá ty dvě metody.
- **Dva endpointy**: `/health/live` (predicate `_ => false` → liveness nezávisí
  na downstreamu) a `/health/ready` (predicate na tag `ReadyTag` = `"ready"`).
  Obojí anonymní.
- **`RedisHealthCheck`** (tag `ready`) se registruje **jen v Redis větvi** —
  readiness závisí na Redisu jen když na něm reálně stojí buffer.
- **Detail výstupu jen v Development** (`HealthCheckResponseWriter`); v produkci
  plain-text, ať anonymní endpoint neprozradí závislosti.
- **Nový check:** `IHealthCheck` do `Api/Health/`, registruj v `AddAppHealthChecks`
  s tagem `ReadyTag`, má-li bránit readiness.

### OpenAPI + interaktivní UI
- Vše v `Api/OpenApi/OpenApiExtensions.cs`: `AddOrderAggregatorOpenApi()` +
  `MapOrderAggregatorOpenApi()` (mapuje `/openapi/v1.json`, Scalar, Swagger UI,
  root redirect na `/swagger`).
- **Popisy operací jsou inline (anglicky)** přes `WithSummary`/`WithDescription`
  na endpointech; ostatní texty inline v transformerech. Lokalizace dokumentace
  byla záměrně odstraněna — lokalizují se jen runtime chybové hlášky.
- **Popisy schémat plynou z XML doc komentářů** na wire DTO v `Contracts`
  (`<GenerateDocumentationFile>true</…>` tam zapnuto) — source generator je
  propíše do `components.schemas`. Api projekt `GenerateDocumentationFile`
  zapnuté **nemá** schválně (jinak CS1591 přes `TreatWarningsAsErrors`).
  **Nový wire DTO** → dokumentuj `/// <summary>` (+ `/// <param>` u recordů).
- **Nový transformer** → do `Api/OpenApi/` + registruj v `AddOrderAggregatorOpenApi`.

### Mapping přes Mapster
- Konfigurace v `Api/Mapping/ContractMappingRegister.cs` (`IRegister`).
- `AddContractMapping()` volá `TypeAdapterConfig.Compile()` při startu → chybný
  mapping fail-fast shodí host. `IMapper` je **singleton** (Mapster je stateless).

### Lokalizace — silně typová, bez `IStringLocalizer`
- Chybové hlášky v `OrderAggregator.Resources/` jako `.resx`: `ApiMessages.resx`
  (en, neutrální) + `ApiMessages.cs.resx` (čeština). Přístup výhradně přes
  generovanou třídu `ApiMessages` (silně typové property → smazaný klíč shodí
  build). Placeholdery přes `string.Format(CultureInfo.CurrentCulture, …)`.
- **Generování při buildu** přes Roslyn source generator
  `Catglobe.ResXFileCodeGenerator` (žádný `.Designer.cs`, žádný commit).
  Konfigurace = MSBuild properties v `OrderAggregator.Resources.csproj`;
  **`UseDefaults` nezapínat** (potlačil by satelitní assembly).
- Runtime výběr kultury v `AddAppLocalization()` přes options pattern; `Program.cs`
  jen `app.UseRequestLocalization()`. Podporované kultury = `SupportedCultures`
  v `LocalizationConstants` (`Shared/Const/`) — **nový jazyk přidej i sem**
  (čte to i `CultureParameterDocumentTransformer` pro OpenAPI dropdown).
- **Nový text:** `data` do `ApiMessages.resx` i všech `ApiMessages.<culture>.resx`,
  pak `ApiMessages.<Klíč>`. **Lokalizují se jen chybové hlášky**, ne dokumentace.

### API key autentizace
- Vlastní `AuthenticationHandler<>` v `Api/Authentication/`,
  `CryptographicOperations.FixedTimeEquals` (žádné timing leaks).
- Klíče v `ApiKey:Keys[]` (multiple), header `X-Api-Key` (konfigurovatelný).
- **Validace při startu**: `ValidateDataAnnotations()` nejde do prvků kolekce,
  proto pravidla (neprázdné `Name`, `Key` ≥ 8 znaků) vynucuje explicitní
  `.Validate(...)` v `AddApiKeyAuthentication` → slabý klíč shodí start.
  `appsettings.json` má jen dev placeholdery (rotovat přes user-secrets/ENV).
- OpenAPI security scheme přidává `ApiKeySecuritySchemeDocumentTransformer`.

### Validace produktu při ingestu
- `POST /api/orders` ověří každý `productId` proti `IProductRepository.Exists()`.
  **All-or-nothing**: jediný neznámý productId odmítne celou dávku 400 +
  ProblemDetails (indexy `[i].productId`) → klient může bezpečně poslat dávku
  znovu bez dvojího započtení.
- Katalog (`JsonFileProductRepository`) je lazy singleton; `Program.cs` po
  `builder.Build()` volá `app.EnsureProductCatalogLoaded()` → **fail-fast** load
  souboru při startu, ne při prvním requestu.

### Ingest hot-path je optimalizovaný (ověřeno load testem)
- `AcceptOrdersAsync` (`Endpoints/OrdersEndpoints.cs`) **nepoužívá** reflexní
  `Validator.TryValidateObject` (alokovala na každou položku) — ruční inline
  kontrola (zero-alloc na happy path). DataAnnotations atributy na `OrderRequest`
  zůstávají jako smlouva pro OpenAPI/klienty, jen se na hot-path neexekuují reflexí.
- **JSON přes source generator:** `AppJsonSerializerContext` (`Api/Serialization/`,
  camelCase) vložen na začátek `TypeInfoResolverChain` → (de)serializace bez
  reflexe. **Nový serializovaný wire typ** → přidej `[JsonSerializable]`.
- Měřený dopad: čistý strop ~4 000 → ~8 000 req/s, p99 ~32 ms.

## Konvence

- **Cokoli přes API hranici patří do `Contracts`.** Domain entity v `Models` se
  neserializují přímo — vždy přes Mapster do DTO.
- **DI extensions** v `Api/DependencyInjection/`, skupinová `Add*` metoda per
  koncept (`AddOrderAggregation`, `AddProductCatalog`, `AddAppObservability`…).
- **Options pattern** všude: `Add<Options>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`.
- **Testy bez `FluentAssertions`** (licence v8) — čisté `Assert.*`.
- **Komentáře v kódu anglicky** (vysvětlují *proč*, ne *co*). Anglicky i OpenAPI
  popisy. Česky zůstávají jen texty pro recenzenta: README a `.resx` pro cs kulturu.

## Soubory v repu (mimo `src/`)

- `readme.md` — uživatelská dokumentace (CZ, pro recenzenta)
- `task-description.md` — zadání
- `LICENSE`

## Jazyk

Uživatel pracuje v češtině; komunikace česky, identifikátory/komentáře v kódu
anglicky. README česky.

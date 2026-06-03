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
├── OrderAggregator.Abstractions/   rozhraní (IOrderStore, IAggregatedOrderSender, IProductRepository, IDeadLetterWriter, IDeadLetterReader) — zná jen Models
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
dotnet test                                   
dotnet run --project OrderAggregator.Api

# Load test (vyžaduje běžící API; env LOADTEST_URL / LOADTEST_API_KEY / LOADTEST_RPS / LOADTEST_DURATION)
dotnet run -c Release --project OrderAggregator.LoadTests

# Observabilita — Aspire Dashboard (UI :18888, OTLP/gRPC :4317); spusť před API
docker run --rm -it -p 18888:18888 -p 4317:18889 \
  -e DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true \
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

### Docker Compose (primární dev launcher)

```bash
docker compose up --build       # API + Redis + Aspire jedním příkazem
docker compose down             # zastavení stacku
```

- `docker-compose.yml` (kořen repa) + `src/OrderAggregator.Api/Dockerfile`
  (multi-stage, build context `src/`) + `src/.dockerignore`. Nahrazuje dřívější
  `run-app.bat`.
- API běží v kontejneru **jen na HTTP** (`http://localhost:5047`, `ASPNETCORE_URLS=http://+:5047`).
  HTTPS/dev certifikáty se v kontejneru neřeší — TLS je věc reverse proxy v produkci.
- Compose **přebíjí konfiguraci env vary** (`Section__Key`), ať kontejnery míří na
  sebe po interní síti místo `localhost`:
  - `OrderStore__Redis__ConnectionString=redis:6379`
  - `OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire:18889` (interní OTLP port kontejneru je
    **18889**, ne hostový 4317)
  - `DeadLetter__Directory=/data/deadletter` (Windows cesta z `appsettings.Development.json`
    v Linux kontejneru neplatí; mapováno na pojmenovaný volume `deadletter-data` →
    replay přežije restart)
  - `AggregatedOrderSender__Console__FailureProbability=0.5` (záměrná fault injection
    pro demo dead-letteru; `0` ji vypne)
- API service má healthcheck na `/health/live`; `depends_on` čeká na `redis`
  (`condition: service_healthy`). Runtime image proto v Dockerfile doinstalovává `curl`.
- **Nová env proměnná / služba** → přidej do `docker-compose.yml`; nový NuGet/projekt
  se v `Dockerfile` projeví sám (restore kopíruje všechny produkční `*.csproj`).

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
- **Docker-dependent testy** (`RedisOrderStoreTests`): + fixture `RedisFixture`, která staví kontejner
  **uvnitř `InitializeAsync`** (ne v ctor — `RedisBuilder` řeší Docker eagerly).
 

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
- `IOrderStore` — agregační buffer, čtyři implementace v `Services/Stores/`,
  výběr přes `OrderStore:Kind` (`InMemory` | `Redis` | `Sqlite` | `SqliteGroupCommit`):
  - **`InMemoryOrderStore`** (default) — jeden `Lock` + plain `Dictionary` swap
    (drain prohodí referenci za prázdný slovník). Load testem ověřeno jako
    rychlejší i jednodušší než `ReaderWriterLockSlim` + `ConcurrentDictionary`.
  - **`RedisOrderStore`** — buffer je Redis hash (`HINCRBY` per productId).
    `AddAsync` přes `MULTI`/`EXEC` transakci (all-or-nothing, retry nezapočte
    dvakrát). Drain: atomický `RENAME` stranou + `HGETALL` + `DEL`; na snapshot
    klíč se hned po `RENAME` nasadí TTL (`SnapshotOrphanTtl`, 1 h) jako pojistka —
    pád mezi `RENAME` a `DEL` tak klíč nenechá v Redisu navždy, sám expiruje. Klíč je
    **per-instance** (`{HashKey}:{InstanceId}`) — víc instancí nad sdíleným
    Redisem si nelezou do dat, každá posílá vlastní snapshot. `InstanceId`
    z konfigurace, prázdný → `Environment.MachineName`. Přežije restart.
    `IConnectionMultiplexer` je singleton (registruje se jen pro `Redis`).
  - **`SqliteOrderStore`** — buffer je jeden lokální SQLite soubor (tabulka
    `buffer(product_id, quantity)`). Nejjednodušší trvalé úložiště **bez serveru**
    pro lokální/single-instance běh. `AddAsync` přičítá přes UPSERT
    (`ON CONFLICT … DO UPDATE SET quantity = quantity + excluded.quantity`) v jedné
    transakci na request; drain je atomický `SELECT` + `DELETE` v jedné transakci
    (obdoba Redisova `RENAME` stranou — nic se neztratí mezi čtením a vynulováním).
    Jedno otevřené `SqliteConnection`, zápisy serializované `SemaphoreSlim` (SQLite
    má jednoho zapisovatele; Microsoft.Data.Sqlite nedovolí překryté commandy).
    `PRAGMA journal_mode=WAL` + `synchronous=FULL` (fsync na každý commit = max
    durabilita, volba uživatele) + `busy_timeout`. Schéma se zakládá v ctoru
    (fail-fast při startu). **Recovery**: soubor přežije restart, `CREATE TABLE
    IF NOT EXISTS` + otevření existujícího souboru → zbylý buffer se příští flush
    odešle. **Lokální per-proces** (jako dead-letter adresář) — víc procesů nad
    jedním souborem je mimo rozsah (oba by drainovali → dvojí odeslání); na sdílené
    nasazení je Redis. `INTEGER` je 64-bit → součty nad `int.MaxValue` projdou.
    Konfig sekce `OrderStore:Sqlite:DataSource` (default `Data/order-buffer.db`).
    Strop ~500 req/s (= fsync rychlost disku, jeden commit/request).
  - **`SqliteGroupCommitOrderStore`** — stejný soubor i durabilita jako `Sqlite`,
    ale **group-commit**: jeden writer thread (`Channel<WriteOp>` + smyčka v ctoru
    spuštěná `Task.Run`) slévá všechny requesty, co dorazí během probíhajícího
    commitu, do **jedné transakce = jednoho fsyncu**. `AddAsync`/`SnapshotAndClear`
    jsou work-items ve frontě (connection je tak single-threaded **bez zámku**);
    drain coalesce zastaví na snapshotu (FIFO pořadí). `AddAsync` se dokončí až po
    commitu na disk → klient nikdy nedostane ACK na ztracená data (neacknutý zápis
    se neztratí tiše, jen se nepotvrdí a klient ho retrynne se stejným `BatchId`).
    `TaskCompletionSource(RunContinuationsAsynchronously)`, aby continuation
    nezablokovala writer. Selhání commitu faultne celou dávku najednou (nezdvojí
    počty). `DisposeAsync`: `Writer.Complete()` → dočká writer (dodraní frontu) →
    zavře spojení. **Výběr OrderStore:Kind=SqliteGroupCommit; reuse `OrderStore:Sqlite`
    options** (stejný DB soubor) — záměrně samostatný provider, ať jde A/B testovat
    proti write-through `Sqlite` stejným load testem.
    - **Společný kód** obou SQLite storeů v `SqliteBuffer` (internal static):
      `OpenInitialized` (otevření + WAL/FULL pragma + DDL), `ApplyAsync`
      (UPSERT dávky v jedné transakci), `SnapshotAndClearAsync`. Storey se liší
      jen *jak* serializují přístup ke spojení (semaphore × writer thread).
    - **Naměřeno** (load test, lokální SSD): write-through `Sqlite` čisté ~300 req/s,
      strop ~500; group-commit komfortně ~16 000 req/s (p99 27 ms, 0 selhání), koleno
      ~24 000 req/s — pak už limituje HTTP/socket vrstva, ne disk. ~32–50× zrychlení
      při stejné durabilitě. Viz paměť `sqlite-store-throughput`.
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
- **Graceful shutdown**: flush loop dělá při vypnutí finální drain + send, ale jen
  pokud od posledního odeslání uplynul aspoň jeden interval (`ShouldFinalFlush` přes
  `TimeProvider`) — finální drain tak neporuší kontrakt „≤ 1 odeslání / 20 s". Zbytek
  bufferu si přebere trvalý store (Redis) při příštím startu; u in-memory je okno
  shodné s tím, co stejně ztrácí restartem. `Program.cs` má
  `HostOptions.ShutdownTimeout = 30 s`, ať se to stihne.

### Dead-letter replay (postupné doodeslání)
- Bez čtenáře by se soubory v dead-letteru hromadily donekonečna. `DeadLetterReplayService`
  (hosted service v `Services/DeadLettering/`) je proto **postupně** doodesílá.
  Zrcadlí flush service: `PeriodicTimer(ReplayInterval, TimeProvider)`, graceful loop,
  `internal ReplayOnceAsync` pro unit testy. Registruje se jen když `DeadLetter:ReplayEnabled`
  (default true).
- **Read side** `IDeadLetterReader` (Abstractions) / `FileDeadLetterReader` (Services):
  enumeruje `deadletter-*.json` (glob **musí** končit `.json`, jinak by chytal writerův
  `.json.tmp`), **FIFO** podle názvu (timestamp prefix sortí chronologicky), čte stejnými
  `JsonSerializerDefaults.Web` jako writer. `DeadLetterEntry` (Models) je opaque handle
  (jméno souboru), který přežije přesun do karantény. Nečitelný JSON → `ReadAsync` vrátí
  `null` = corrupt.
- **Throttle = `MaxFilesPerRun` na tick + `ReplayInterval` mezi ticky.** Jeden pokus
  `SendAsync` na soubor na tick (interval = backoff, žádná vnitřní retry smyčka — to ji
  liší od flush service). Úspěch → `DeleteAsync` + metrika. Selhání → **in-memory čítač**
  pokusů (`Dictionary` ve službě, restart vynuluje — karanténa je pojistka, ne exactly-once);
  po `MaxReplayAttempts` → `QuarantineAsync` přesune soubor do `DeadLetter:PoisonDirectory`
  (default `poison`, podadresář pod `Directory`), aby neblokoval frontu. Čítač sdílí
  jedna cesta (`RegisterFailedAttemptAsync`) pro **selhaný send i jakoukoli neočekávanou
  chybu** (nečitelný payload mimo filtr `ReadAsync`, odepřený přesun do karantény) →
  i trvale rozbitý soubor nakonec skončí v karanténě místo nekonečného opakování;
  samotné selhání karantény se zaloguje a smyčku neshodí.
- **Re-send přímo přes `IAggregatedOrderSender`** (ne re-enqueue do storu) → zachová původní
  dávku i `FlushedAt`, nezdvojí počty.
- **Idempotency key — `OrderBatch.BatchId` (Guid).** Pravá transakce přes downstream + filesystem
  neexistuje, takže pipeline je záměrně **at-least-once** (pošli, pak teprve smaž → radši
  duplicita než ztráta). `BatchId` se generuje jednou při flushi, **round-tripuje** dead-letter
  souborem (writer ho dá i do názvu `deadletter-{flushedAt}-{batchId:N}.json`) a posílá se
  stejný na každý retry i replay → downstream, který na něm deduplikuje, dostane efektivně
  exactly-once. `ConsoleSender` ho jen loguje; reálný HTTP/Kafka sender by ho dal do
  `Idempotency-Key` hlavičky / message key. Selže-li `DeleteAsync` po úspěšném sendu, příští
  tick pošle dávku znovu se **stejným** `BatchId` → bezpečné.
- **Mimo rozsah** (zdokumentováno jako rozšíření): multi-instance/crash safety přes
  claim-by-rename — dnes je dead-letter adresář lokální per-instance.
- Konfig sekce `DeadLetter`: `ReplayEnabled`, `ReplayIntervalSeconds` (default 30),
  `MaxFilesPerRun` (10), `MaxReplayAttempts` (5), `PoisonDirectory` (`poison`).

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
    `flush.duration` (ms), `flush.sent`, `flush.dead_lettered`,
    `deadletter.replayed`, `deadletter.quarantined`.
  - Flush i replay service berou `OrderAggregationMetrics?` jako **volitelný** ctor
    parametr (vzor jako `TimeProvider?`) → unit testy bez meteru, DI dodá reálnou instanci.
    Replay span = `deadletter.replay` přes stejný `ActivitySource`.
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
  - **`IConnectionMultiplexer` se připojuje s `AbortOnConnectFail=false`** (Redis větev
    v `RegisterOrderStore`). Cold start s dočasně nedostupným Redisem tak **nevyhodí**
    výjimku už při resolve multiplexeru (to by `/health/ready` shodilo na **500**) —
    multiplexer zůstane v retry stavu a `RedisHealthCheck` vrátí `Unhealthy` → **503**,
    což je korektní readiness signál (orchestrátor instanci nepustí do rotace a zkouší dál).
  - **Test matrix** (`HealthEndpointTests` InMemory→200; `HealthReadinessRedisTests`
    Redis-up→200 přes `RedisFixture`, Redis-down→503). Pozn.: store-kind switch se čte
    **eagerly při registraci**, takže test override přes `ConfigureAppConfiguration`
    na něj nedosáhne — Redis wiring se v `OrderAggregatorTestFactory` upravuje přes
    `ConfigureServices` (strip checku / přepnutí multiplexeru), ne přes konfiguraci.
- **Detail výstupu jen v Development** (`HealthCheckResponseWriter`); v produkci
  plain-text, ať anonymní endpoint neprozradí závislosti.
- **Nový check:** `IHealthCheck` do `Api/Health/`, registruj v `AddAppHealthChecks`
  s tagem `ReadyTag`, má-li bránit readiness.

### OpenAPI + interaktivní UI
- Vše v `Api/OpenApi/OpenApiExtensions.cs`: `AddAppOpenApi()` +
  `MapOpenApi()` (mapuje `/openapi/v1.json`, Scalar, Swagger UI,
  root redirect na `/swagger`). Microsoft.OpenApi **2.0** (`OpenApiSchema.Type` je
  flags enum `JsonSchemaType`, schémata jsou `IOpenApiSchema` — mutace přes cast na
  konkrétní `OpenApiSchema`).
- **Popisy operací jsou inline (anglicky)** přes `WithSummary`/`WithDescription`
  na endpointech; ostatní texty inline v transformerech. Lokalizace dokumentace
  byla záměrně odstraněna — lokalizují se jen runtime chybové hlášky.
- **`OrderAggregatorDocumentTransformer`** plní `info` (title/version/description,
  `contact`, MIT licence) a **popisy tagů** `Orders`/`Products` (`document.Tags`,
  jména musí sednout na `.WithTags(...)`).
- **`OrdersRequestOperationTransformer`** tvaruje request body `POST /api/orders`:
  `Required = true`, rozbalí nullable `oneOf [null, array]` na čisté pole s
  `minItems: 1` (odpovídá tomu, že handler odmítne null/prázdno 400) a vkládá
  ukázkové payloady do „Try it out".
- **Endpointy deklarují `500` ProblemDetails** (`.ProducesProblem(500)`) vedle
  401/404 — kontrakt přiznává i serverovou chybu.
- **`ProblemDetailsSchemaTransformer`** doplňuje `description` frameworkovým
  schématům `ProblemDetails` (401/404/500) a `HttpValidationProblemDetails` (400 +
  mapa `errors`) — ta nejsou naše dokumentovaná DTO, jinak by v `components.schemas`
  zůstala bez popisu. Detekce přes `context.JsonTypeInfo.Type` (exact match).
- **Popisy schémat plynou z XML doc komentářů** na wire DTO v `Contracts`
  (`<GenerateDocumentationFile>true</…>` tam zapnuto) — source generator je
  propíše do `components.schemas`. Api projekt `GenerateDocumentationFile`
  zapnuté **nemá** schválně (jinak CS1591 přes `TreatWarningsAsErrors`).
  **Nový wire DTO** → dokumentuj `/// <summary>` (+ `/// <param>` u recordů).
- **Nový transformer** → do `Api/OpenApi/` + registruj v `AddAppOpenApi`
  (`AddDocumentTransformer` / `AddOperationTransformer` / `AddSchemaTransformer`).

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
- **Striktní čísla:** `ConfigureHttpJsonOptions` v `Program.cs` nastavuje
  `NumberHandling = JsonNumberHandling.Strict`. Web default `AllowReadingFromString`
  by jinak (a) připustil čísla poslaná jako string a (b) prosákl do OpenAPI jako
  union `["integer","string"]` + číselný `pattern` u `quantity`/`count`/`status`.
  Strict drží kontrakt i přijímaný formát čistý (číslo = JSON number, ne `"5"`).
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

## Čistý kód — povinná konvence

Veškerý nový i upravovaný kód musí dodržovat principy čistého kódu. Tohle není
„nice to have", je to **vstupní podmínka pro merge**.

- **DRY (Don't Repeat Yourself)** — žádné copy-paste. Opakovaný kód/logiku
  vytáhni do sdílené metody, helperu nebo extension. Pozor i na duplicitu
  *znalosti* (magické konstanty, stejná validace na dvou místech).
- **KISS (Keep It Simple)** — nejjednodušší řešení, které splní požadavek.
  Žádná spekulativní abstrakce „pro budoucnost" (YAGNI). Abstrakci přidej až
  ji reálně potřebuješ (vzor: pluggable `IOrderStore`/`IAggregatedOrderSender`
  vznikl z konkrétní potřeby, ne preventivně).
- **SOLID**:
  - *SRP* — jedna třída/metoda = jedna odpovědnost a jeden důvod ke změně
    (viz oddělení store / sender / flush service / dead-lettering).
  - *OCP* — rozšiřuj přidáním nové větve/třídy, ne přepisem stávající
    (switche `RegisterOrderStore`/`RegisterAggregatedOrderSender`).
  - *LSP* — implementace rozhraní musí být zaměnitelné bez překvapení.
  - *ISP* — malá, cílená rozhraní (`IDeadLetterWriter` × `IDeadLetterReader`
    odděleně, ne jeden tlustý interface).
  - *DIP* — závisíme na abstrakcích z `Abstractions/`, ne na konkrétních typech;
    konkrétní implementace dodává až kompoziční kořen (`Api`).
- **Malé metody** — metoda dělá jednu věc, vejde se na obrazovku, minimum
  vnořených úrovní. Dlouhou metodu rozsekej na pojmenované privátní kroky.
- **Vypovídající názvy** — odhalí záměr (`AcceptOrdersAsync`, `ReplayOnceAsync`).
  Žádné zkratky bez kontextu, žádné `tmp`/`data2`. Název > komentář.
- **Komentáře vysvětlují „proč", ne „co"** — kód čitelný sám o sobě;
  komentář jen tam, kde je nutný kontext rozhodnutí (anglicky, viz Konvence).
- **Guard clauses & early return** — místo hluboké pyramidy `if`ů; nešťastnou
  cestu řeš na začátku, happy path nech plochou.
- **Žádné mrtvé/zakomentované bloky** — nepoužitý kód smaž (historie je v gitu).
- **Konzistence se stávajícím kódem** — nový kód vypadá jako okolní (idiom,
  pojmenování, hustota komentářů). Při pochybnostech kopíruj zavedený vzor.

Build vynucuje kvalitu i strojově: `TreatWarningsAsErrors=true` (warning = chyba)
a ArchUnitNET testy hlídají vrstvy. Čistý kód jde **nad rámec** těchto kontrol —
splň oboje.

## Soubory v repu (mimo `src/`)

- `readme.md` — uživatelská dokumentace (CZ, pro recenzenta)
- `task-description.md` — zadání
- `docker-compose.yml` — dev stack (API + Redis + Aspire); primární launcher
- `src/OrderAggregator.Api/Dockerfile` + `src/.dockerignore` — build image API
- `LICENSE`

## Jazyk

Uživatel pracuje v češtině; komunikace česky, identifikátory/komentáře v kódu
anglicky. README česky.

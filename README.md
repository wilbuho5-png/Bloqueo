# Bloqueo de ligas deportivas – [Betradar].[dbo].[Feeds] (C# / SQL Server)

Función en C# que **bloquea mercados (columnas de cuotas)** de unas **ligas** dadas
(campo `IdLigas`, admite varias) en la tabla `dbo.Feeds` y devuelve un **`DataSet`
con todas las cuotas excepto las bloqueadas**.

## Archivos

| Archivo            | Descripción                                                        |
|--------------------|--------------------------------------------------------------------|
| `LeagueBlocker.cs` | Clase principal `Betradar.Blocking.LeagueBlocker` + `LeagueBlockResult` |
| `UsageSamples.cs`  | Ejemplos de uso                                                    |
| `LigaBlocker.csproj` | Proyecto .NET 8 (`System.Data.SqlClient`)                        |

Compilación verificada: `dotnet build` → **0 errores, 0 advertencias**.

## Tests

Proyecto de tests xUnit en `tests/` (19 tests unitarios, todos en verde):

```bash
cd tests
dotnet test          # → Passed! Failed: 0, Passed: 19
```

Qué cubren (`tests/LeagueBlockerTests.cs`):

| Clase de test | Qué valida |
|---|---|
| `ValidationTests` | Cadena de conexión vacía/nula, ligas nulas/vacías/≤0 → excepciones antes de tocar la BD |
| `HelperTests` | `MatchColumns`: sólo mercados existentes, case-insensitive, deduplicado, ignora vacíos |
| `WhereClauseTests` | `BuildWhere`: `WHERE [IdLigas] IN (@lg0,@lg1)` parametrizado (sin inyección SQL) + filtros de deporte/fechas |
| `ResultAndPreviewTests` | `LeagueBlockResult.RowCount` y el anulado en memoria de las columnas bloqueadas |

Para probar contra una base de datos real usa `tests/TestDbScript.sql`
(crea `BetradarTest` con `[dbo].[Feeds]` y 4 partidos de ejemplo; luego
ejecuta `BlockLeagues(new[]{1234,5678})` y verifica que las cuotas de esas
ligas quedan a `NULL` y la liga `9999` queda intacta).

## Cómo funciona

1. Descubre dinámicamente las columnas de mercado consultando `sys.columns`
   (excluye `NSS, IdSport, IdLigas, IdLocalidad, Nombre, NombreLiga, GameId,
   idTeam, TeamName, status, isHome, GameDate, timestmp`).
2. Ejecuta un `UPDATE dbo.Feeds SET [mercado] = NULL, ... WHERE [IdLigas] IN (...)`
   dentro de una transacción → eso es "bloquear" el mercado.
3. Ejecuta un `SELECT * FROM dbo.Feeds WHERE [IdLigas] IN (...)` y lo devuelve
   como `DataSet`: contiene **todas las cuotas menos las bloqueadas** (a `NULL`).

Todo parametrizado (sin riesgo de SQL injection); los nombres de columna se
validan contra el esquema real y se delimitan con corchetes.

## Uso rápido

```csharp
var blocker = new LeagueBlocker("Server=...;Database=Betradar;...");

// Varias ligas, todos los mercados:
DataSet ds = blocker.BlockLeagues(new List<int> { 1234, 5678, 9012 }).Data;

// Sólo mercados concretos y rango de fechas:
DataSet ds2 = blocker.BlockLeagues(
    leagueIds:      new[] { 1234 },
    marketsToBlock: new[] { "mHandicapML", "mTotalesML" },
    dateFrom:       DateTime.Today,
    dateTo:         DateTime.Today.AddDays(7)).Data;

// Previsualización SIN tocar la BD (nullifica en memoria):
DataSet preview = blocker.PreviewBlockedLeagues(new[] { 1234, 5678 }).Data;
```

El objeto `LeagueBlockResult` expone: `Data` (DataSet), `BlockedMarkets`
(columnas anuladas), `RowsAffected` y `RowCount`.

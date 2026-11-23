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

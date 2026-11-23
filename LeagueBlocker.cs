using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace Betradar.Blocking
{
    /// <summary>
    /// Resultado del bloqueo: el DataSet con los partidos/cuotas y el detalle de lo bloqueado.
    /// </summary>
    public sealed class LeagueBlockResult
    {
        /// <summary>DataSet con todas las cuotas, con las columnas (mercados) bloqueadas a NULL.</summary>
        public DataSet Data { get; set; }

        /// <summary>Nombres de las columnas/mercados que se bloquearon.</summary>
        public List<string> BlockedMarkets { get; set; } = new List<string>();

        /// <summary>Número de filas afectadas por el UPDATE en base de datos.</summary>
        public int RowsAffected { get; set; }

        /// <summary>Filas devueltas en el DataSet.</summary>
        public int RowCount => (Data != null && Data.Tables.Count > 0) ? Data.Tables[0].Rows.Count : 0;
    }

    /// <summary>
    /// Bloquea mercados (columnas de cuotas) de la tabla [Betradar].[dbo].[Feeds]
    /// para un conjunto de ligas ([IdLigas]) y devuelve un DataSet con todas las
    /// cuotas excepto las bloqueadas.
    /// </summary>
    public class LeagueBlocker
    {
        private readonly string _connectionString;

        /// <summary>
        /// Columnas que NUNCA deben bloquearse (identidad / metadatos del partido).
        /// Cualquier columna de cuota es candidata a bloqueo.
        /// </summary>
        private static readonly HashSet<string> NonMarketColumns =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "NSS", "IdSport", "IdLigas", "IdLocalidad", "Nombre",
                "NombreLiga", "GameId", "idTeam", "TeamName", "status",
                "isHome", "GameDate", "timestmp"
            };

        public LeagueBlocker(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("La cadena de conexión es obligatoria.", nameof(connectionString));
            _connectionString = connectionString;
        }

        // ------------------------------------------------------------------
        //  API principal
        // ------------------------------------------------------------------

        /// <summary>
        /// Bloquea las ligas indicadas usando TODOS los mercados disponibles y
        /// devuelve el DataSet sin esas cuotas.
        /// </summary>
        /// <param name="leagueIds">Id(s) de liga a bloquear (campo [IdLigas]).</param>
        /// <param name="transactional">true = usa transacción (recomendado).</param>
        public LeagueBlockResult BlockLeagues(IEnumerable<int> leagueIds, bool transactional = true)
        {
            return BlockLeagues(leagueIds, marketsToBlock: null, sportId: null,
                                dateFrom: null, dateTo: null, transactional: transactional);
        }

        /// <summary>
        /// Bloquea mercados concretos (o todos si <paramref name="marketsToBlock"/> es null/vacío)
        /// para las ligas indicadas. El UPDATE pone a NULL las columnas bloqueadas y el
        /// DataSet devuelto contiene todas las cuotas excepto las bloqueadas.
        /// </summary>
        /// <param name="leagueIds">Id(s) de liga a bloquear (campo [IdLigas]). Varios valores permitidos.</param>
        /// <param name="marketsToBlock">Nombres exactos de columnas/mercados a bloquear. Null = todos.</param>
        /// <param name="sportId">Opcional: restringe el bloqueo a un deporte ([IdSport]).</param>
        /// <param name="dateFrom">Opcional: sólo partidos desde esta fecha.</param>
        /// <param name="dateTo">Opcional: sólo partidos hasta esta fecha.</param>
        /// <param name="transactional">true = ejecuta UPDATE + SELECT dentro de una transacción.</param>
        public LeagueBlockResult BlockLeagues(IEnumerable<int> leagueIds,
                                              IEnumerable<string> marketsToBlock,
                                              int? sportId = null,
                                              DateTime? dateFrom = null,
                                              DateTime? dateTo = null,
                                              bool transactional = true)
        {
            if (leagueIds == null) throw new ArgumentNullException(nameof(leagueIds));

            var ids = leagueIds.Where(i => i > 0).Distinct().ToList();
            if (ids.Count == 0)
                throw new ArgumentException("Debe indicar al menos una liga (IdLigas) a bloquear.", nameof(leagueIds));

            var result = new LeagueBlockResult();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // 1) Descubrir dinámicamente las columnas de mercado reales de la tabla.
                var allMarketColumns = GetMarketColumns(conn);

                // 2) Validar/refinar qué mercados se van a bloquear.
                List<string> blocked;
                if (marketsToBlock == null || !marketsToBlock.Any())
                {
                    blocked = allMarketColumns;                       // todos los mercados
                }
                else
                {
                    var known = new HashSet<string>(allMarketColumns, StringComparer.OrdinalIgnoreCase);
                    blocked = marketsToBlock
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .Select(m => m.Trim())
                        .Where(known.Contains)                            // ignora nombres inexistentes
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (blocked.Count == 0)
                        throw new ArgumentException(
                            "Ninguno de los mercados indicados existe como columna de cuota en [dbo].[Feeds].",
                            nameof(marketsToBlock));
                }

                result.BlockedMarkets = blocked;

                using (var tx = transactional ? conn.BeginTransaction(IsolationLevel.ReadCommitted) : null)
                {
                    try
                    {
                        // 3) UPDATE: anula (bloquea) las columnas de mercado de las ligas dadas.
                        result.RowsAffected = ExecuteBlockUpdate(conn, tx, ids, blocked, sportId, dateFrom, dateTo);

                        // 4) SELECT: devuelve el DataSet con todas las cuotas menos las bloqueadas.
                        result.Data = ExecuteSelect(conn, tx, ids, sportId, dateFrom, dateTo);

                        if (tx != null) tx.Commit();
                    }
                    catch
                    {
                        if (tx != null) tx.Rollback();
                        throw;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Versión "de sólo lectura": NO modifica la base de datos. Devuelve el DataSet con
        /// las columnas de mercado de las ligas bloqueadas puestas a NULL en memoria.
        /// Útil para previsualizar el efecto del bloqueo o para servir cuotas filtradas.
        /// </summary>
        public LeagueBlockResult PreviewBlockedLeagues(IEnumerable<int> leagueIds,
                                                       IEnumerable<string> marketsToBlock = null,
                                                       int? sportId = null,
                                                       DateTime? dateFrom = null,
                                                       DateTime? dateTo = null)
        {
            if (leagueIds == null) throw new ArgumentNullException(nameof(leagueIds));
            var ids = leagueIds.Where(i => i > 0).Distinct().ToList();
            if (ids.Count == 0)
                throw new ArgumentException("Debe indicar al menos una liga (IdLigas).", nameof(leagueIds));

            var result = new LeagueBlockResult();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                var allMarketColumns = GetMarketColumns(conn);
                List<string> blocked = (marketsToBlock == null || !marketsToBlock.Any())
                    ? allMarketColumns
                    : MatchColumns(allMarketColumns, marketsToBlock);

                if (blocked.Count == 0)
                    throw new ArgumentException(
                        "Ninguno de los mercados indicados existe como columna de cuota en [dbo].[Feeds].",
                        nameof(marketsToBlock));

                result.BlockedMarkets = blocked;
                result.Data = ExecuteSelect(conn, null, ids, sportId, dateFrom, dateTo);
                result.RowsAffected = 0;

                // Anular en memoria las columnas bloqueadas -> el DataSet ya no contiene esas cuotas.
                var tbl = result.Data.Tables[0];
                foreach (var col in blocked)
                {
                    if (tbl.Columns.Contains(col))
                    {
                        foreach (DataRow r in tbl.Rows)
                            r[col] = DBNull.Value;
                    }
                }
            }

            return result;
        }

        // ------------------------------------------------------------------
        //  Helpers
        // ------------------------------------------------------------------

        private static List<string> MatchColumns(List<string> universe, IEnumerable<string> requested)
        {
            var known = new HashSet<string>(universe, StringComparer.OrdinalIgnoreCase);
            return requested
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim())
                .Where(known.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Obtiene las columnas de cuota (mercados) existentes en [dbo].[Feeds].</summary>
        private static List<string> GetMarketColumns(SqlConnection conn)
        {
            const string sql = @"
SELECT c.name
FROM   sys.columns AS c
WHERE  c.object_id = OBJECT_ID('dbo.[Feeds]')
ORDER  BY c.column_id;";

            var cols = new List<string>();
            using (var cmd = new SqlCommand(sql, conn))
            using (var rd = cmd.ExecuteReader())
            {
                while (rd.Read())
                {
                    var name = rd.GetString(0);
                    if (!NonMarketColumns.Contains(name))
                        cols.Add(name);
                }
            }

            if (cols.Count == 0)
                throw new InvalidOperationException(
                    "No se detectaron columnas de mercado en [Betradar].[dbo].[Feeds].");

            return cols;
        }

        /// <summary>Construye la cláusula WHERE común (ligas + filtros opcionales) con parámetros.</summary>
        private static string BuildWhere(List<int> leagueIds, int? sportId,
                                         DateTime? dateFrom, DateTime? dateTo,
                                         SqlParameterCollection parameters)
        {
            // Parámetro de ligas: @p0, @p1, ... (IN seguro, sin concatenación)
            var idParams = new List<string>();
            for (int i = 0; i < leagueIds.Count; i++)
            {
                string p = "@lg" + i;
                idParams.Add(p);
                parameters.AddWithValue(p, leagueIds[i]);
            }

            var where = "WHERE [IdLigas] IN (" + string.Join(",", idParams) + ")";

            if (sportId.HasValue)
            {
                where += " AND [IdSport] = @sport";
                parameters.AddWithValue("@sport", sportId.Value);
            }
            if (dateFrom.HasValue)
            {
                where += " AND [GameDate] >= @dfrom";
                parameters.AddWithValue("@dfrom", dateFrom.Value);
            }
            if (dateTo.HasValue)
            {
                where += " AND [GameDate] <= @dto";
                parameters.AddWithValue("@dto", dateTo.Value);
            }

            return where;
        }

        /// <summary>Ejecuta el UPDATE que anula las columnas de mercado bloqueadas.</summary>
        private static int ExecuteBlockUpdate(SqlConnection conn, SqlTransaction tx,
                                              List<int> leagueIds, List<string> blocked,
                                              int? sportId, DateTime? dateFrom, DateTime? dateTo)
        {
            // Los nombres de columna se blindan con [ ] y se escapan los corchetes internos.
            var sets = blocked.Select(c => "[" + c.Replace("]", "]]") + "] = NULL");

            string sql = "UPDATE dbo.[Feeds]\nSET\n    " + string.Join(",\n    ", sets) + "\n";

            using (var cmd = new SqlCommand(sql, conn, tx))
            {
                cmd.CommandTimeout = 120;
                cmd.CommandText += BuildWhere(leagueIds, sportId, dateFrom, dateTo, cmd.Parameters);
                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Devuelve el DataSet con todas las cuotas (las bloqueadas ya vienen a NULL).</summary>
        private static DataSet ExecuteSelect(SqlConnection conn, SqlTransaction tx,
                                             List<int> leagueIds, int? sportId,
                                             DateTime? dateFrom, DateTime? dateTo)
        {
            using (var cmd = new SqlCommand("SELECT * FROM dbo.[Feeds]", conn, tx))
            {
                cmd.CommandTimeout = 120;
                cmd.CommandText += BuildWhere(leagueIds, sportId, dateFrom, dateTo, cmd.Parameters);

                var ds = new DataSet("BetradarFeeds");
                using (var da = new SqlDataAdapter(cmd))
                {
                    da.Fill(ds, "Feeds");
                }

                ds.Tables[0].TableName = "Feeds";
                return ds;
            }
        }
    }
}

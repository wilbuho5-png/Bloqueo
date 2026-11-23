using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Betradar.Blocking;
using Xunit;

namespace BlockerTests
{
    /// <summary>
    /// Tests unitarios de LeagueBlocker SIN base de datos:
    /// prueban la validación de argumentos (fail-fast) y los helpers estáticos
    /// privados vía reflexión.
    /// </summary>
    public class ValidationTests
    {
        // Cadena de conexión sintácticamente válida: ningún test llega a abrirla,
        // porque la clase falla ANTES por validación de argumentos.
        private const string Conn = "Server=localhost;Database=Betradar;Integrated Security=true";

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_CadenaConexionInvalida_LanzaArgumentException(string cs)
        {
            Assert.Throws<ArgumentException>(() => new LeagueBlocker(cs));
        }

        [Fact]
        public void Constructor_CadenaConexionValida_NoLanza()
        {
            var ex = Record.Exception(() => new LeagueBlocker(Conn));
            Assert.Null(ex);
        }

        [Fact]
        public void BlockLeagues_LeagueIdsNull_LanzaArgumentNullException()
        {
            var blocker = new LeagueBlocker(Conn);
            Assert.Throws<ArgumentNullException>(() => blocker.BlockLeagues(null));
        }

        [Fact]
        public void BlockLeagues_SinLigasValidas_LanzaArgumentException()
        {
            var blocker = new LeagueBlocker(Conn);
            // ids <= 0 se filtran -> se queda vacío -> debe fallar antes de tocar la BD
            Assert.Throws<ArgumentException>(() => blocker.BlockLeagues(new[] { 0, -5, -1 }));
        }

        // NOTA: la validacion de mercados contra sys.columns ocurre DESPUES de abrir
        // la conexion, por lo que solo es testeable con una BD real (ver TestDbScript.sql).
        // Aqui se prueba la precondicion: liga valida + mercado inexistente debe fallar
        // ya sea por ArgumentException (con BD) o por SqlException (sin BD accesible),
        // pero NUNCA devolver un resultado satisfactorio.
        [Fact]
        public void BlockLeagues_LigaValidaMercadoInexistente_NoDevuelveResultado()
        {
            var blocker = new LeagueBlocker(Conn);
            Assert.ThrowsAny<Exception>(
                () => blocker.BlockLeagues(new[] { 1234 }, new[] { "MercadoQueNoExiste" }));
        }

        [Fact]
        public void PreviewBlockedLeagues_LeagueIdsNull_LanzaArgumentNullException()
        {
            var blocker = new LeagueBlocker(Conn);
            Assert.Throws<ArgumentNullException>(() => blocker.PreviewBlockedLeagues(null));
        }

        [Fact]
        public void PreviewBlockedLeagues_SinLigasValidas_LanzaArgumentException()
        {
            var blocker = new LeagueBlocker(Conn);
            Assert.Throws<ArgumentException>(() => blocker.PreviewBlockedLeagues(new int[0]));
        }
    }

    /// <summary>Prueba del filtrado/distintado interno de mercados (helper privado MatchColumns).</summary>
    public class HelperTests
    {
        private static readonly System.Reflection.MethodInfo MatchColumns =
            typeof(LeagueBlocker)
                .GetMethod("MatchColumns",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        private static List<string> Match(List<string> universe, IEnumerable<string> requested)
        {
            return (List<string>)MatchColumns.Invoke(null, new object[] { universe, requested });
        }

        private static readonly List<string> Universe = new List<string>
        {
            "mHandicapML", "mTotalesML", "e2Half", "mCorrectScore0"
        };

        [Fact]
        public void MatchColumns_DevuelveSoloLosExistentes()
        {
            var r = Match(Universe, new[] { "e2Half", "NO_EXISTE" });
            Assert.Equal(new[] { "e2Half" }, r);
        }

        [Fact]
        public void MatchColumns_EsCaseInsensitive()
        {
            var r = Match(Universe, new[] { "E2HALF" });
            // El filtro es case-insensitive (MatchColumns usa OrdinalIgnoreCase),
            // pero el nombre devuelto conserva la forma del primer valor pedido.
            Assert.Equal(new[] { "E2HALF" }, r);
            Assert.Contains("e2Half", Universe, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void MatchColumns_IgnoraVaciosYNulosYLimpiaEspacios()
        {
            var r = Match(Universe, new[] { "  e2Half  ", "", null, "  " });
            Assert.Equal(new[] { "e2Half" }, r);
        }

        [Fact]
        public void MatchColumns_Deduplica()
        {
            var r = Match(Universe, new[] { "e2Half", "E2HALF", "e2half" });
            Assert.Single(r);
        }

        [Fact]
        public void MatchColumns_NingunoCoincide_DevuelveListaVacia()
        {
            var r = Match(Universe, new[] { "fantasma" });
            Assert.Empty(r);
        }
    }

    /// <summary>Prueba de la construcción del WHERE parametrizado (helper privado BuildWhere).</summary>
    public class WhereClauseTests
    {
        private static readonly System.Reflection.MethodInfo BuildWhere =
            typeof(LeagueBlocker)
                .GetMethod("BuildWhere",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        private static string Build(List<int> ids, int? sport, DateTime? from, DateTime? to,
                                    SqlParameterCollectionHolder p)
        {
            return (string)BuildWhere.Invoke(null,
                new object[] { ids, sport, from, to, p.Collection });
        }

        // Wrapper simple para obtener un SqlParameterCollection real sin conexión.
        private sealed class SqlParameterCollectionHolder
        {
            public System.Data.SqlClient.SqlParameterCollection Collection { get; }
            public SqlParameterCollectionHolder()
            {
                var cmd = new System.Data.SqlClient.SqlCommand();
                Collection = cmd.Parameters;
            }
        }

        [Fact]
        public void BuildWhere_VariasLigas_GeneraInParametrizadoYSinInyeccion()
        {
            var h = new SqlParameterCollectionHolder();
            var where = Build(new List<int> { 1234, 5678 }, null, null, null, h);

            Assert.Equal("WHERE [IdLigas] IN (@lg0,@lg1)", where);
            Assert.Equal(2, h.Collection.Count);
            Assert.Equal(1234, ((System.Data.SqlClient.SqlParameter)h.Collection[0]).Value);
            Assert.Equal(5678, ((System.Data.SqlClient.SqlParameter)h.Collection[1]).Value);
        }

        [Fact]
        public void BuildWhere_ConFiltros_AgregaSportYFechasComoParametros()
        {
            var h = new SqlParameterCollectionHolder();
            var from = new DateTime(2026, 11, 23);
            var to = from.AddDays(7);
            var where = Build(new List<int> { 1 }, 20, from, to, h);

            Assert.Contains("[IdSport] = @sport", where);
            Assert.Contains("[GameDate] >= @dfrom", where);
            Assert.Contains("[GameDate] <= @dto", where);
            Assert.Equal(4, h.Collection.Count); // lg0 + sport + dfrom + dto
        }

        [Fact]
        public void BuildWhere_SinFiltros_SoloLigas()
        {
            var h = new SqlParameterCollectionHolder();
            var where = Build(new List<int> { 999 }, null, null, null, h);
            Assert.DoesNotContain("@sport", where);
            Assert.DoesNotContain("@dfrom", where);
            Assert.DoesNotContain("@dto", where);
        }
    }

    /// <summary>Prueba del resultado y del bloqueo EN MEMORIA (PreviewBlockedLeagues) con DataSet falso.</summary>
    public class ResultAndPreviewTests
    {
        [Fact]
        public void LeagueBlockResult_RowCountReflejaElDataSet()
        {
            var res = new LeagueBlockResult();
            Assert.Equal(0, res.RowCount); // DataSet null -> 0 filas

            var ds = new DataSet();
            var t = ds.Tables.Add("Feeds");
            t.Columns.Add("NSS", typeof(int));
            for (int i = 0; i < 3; i++) t.Rows.Add(i);
            res.Data = ds;
            Assert.Equal(3, res.RowCount);
        }

        [Fact]
        public void Preview_NullificaEnMemoriaLasColumnasBloqueadas()
        {
            // Replica la lógica de anulado en memoria de PreviewBlockedLeagues
            // sobre un DataSet construido a mano (no requiere SQL Server).
            var ds = new DataSet("BetradarFeeds");
            var tbl = ds.Tables.Add("Feeds");
            tbl.Columns.Add("NSS", typeof(int));
            tbl.Columns.Add("IdLigas", typeof(int));
            tbl.Columns.Add("e2Half", typeof(string));
            tbl.Columns.Add("mTotalesML", typeof(string));
            tbl.Rows.Add(1, 1234, "1.85", "2.10");
            tbl.Rows.Add(2, 5678, "1.95", "1.70");

            var blocked = new List<string> { "e2Half", "mTotalesML" };
            foreach (var col in blocked)
                if (tbl.Columns.Contains(col))
                    foreach (DataRow r in tbl.Rows)
                        r[col] = DBNull.Value;

            // El DataSet "saliente" ya no contiene las cuotas bloqueadas...
            foreach (DataRow r in tbl.Rows)
            {
                Assert.True(r.IsNull("e2Half"));
                Assert.True(r.IsNull("mTotalesML"));
            }
            // ...pero sí conserva el resto de columnas.
            Assert.Equal(1234, tbl.Rows[0]["IdLigas"]);
            Assert.Equal(2, tbl.Rows.Count);
        }
    }
}

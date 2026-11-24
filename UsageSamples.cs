using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Betradar.Blocking;

namespace Betradar.Blocking.Samples
{
    /// <summary>
    /// Ejemplos de uso de LeagueBlocker. No se compilan como ejecutable principal:
    /// copie los fragmentos a su servicio/web API según necesite.
    /// </summary>
    public static class UsageSamples
    {
        // Cadena de conexión recomendada (SQL Server). Ajuste servidor/seguridad.
        private const string ConnStr =
            "Server=MI_SERVIDOR;Database=Betradar;User Id=sa;Password=*****;MultipleActiveResultSets=True;";

        // ------------------------------------------------------------------
        // 1) Bloquear TODAS las ligas indicadas (varias) en todos los mercados
        //    y obtener el DataSet sin esas cuotas.
        // ------------------------------------------------------------------
        public static DataSet BloquearVariasLigas()
        {
            var blocker = new LeagueBlocker(ConnStr);

            List<int> ligas = new List<int> { 1234, 5678, 9012 };   // varias ligas

            LeagueBlockResult res = blocker.BlockLeagues(ligas);

            Console.WriteLine($"Mercados bloqueados : {res.BlockedMarkets.Count}");
            Console.WriteLine($"Filas afectadas     : {res.RowsAffected}");
            Console.WriteLine($"Filas en DataSet    : {res.RowCount}");

            return res.Data;   // DataSet con todas las cuotas EXCEPTO las bloqueadas
        }

        // ------------------------------------------------------------------
        // 2) Bloquear sólo MERCADOS concretos de unas ligas (p. ej. hándicap y totales)
        // ------------------------------------------------------------------
        public static DataSet BloquearMercadosConcretos()
        {
            var blocker = new LeagueBlocker(ConnStr);

            var mercados = new[]
            {
                "mHandicapML", "mHandicap0", "mHandicap1",
                "mTotalesML",  "mTotales0",  "mTotales1"
            };

            LeagueBlockResult res = blocker.BlockLeagues(
                leagueIds:       new List<int> { 1234 },
                marketsToBlock:  mercados,
                sportId:         null,
                dateFrom:        DateTime.Today,                 // sólo desde hoy
                dateTo:          DateTime.Today.AddDays(7));     // hasta en 7 días

            return res.Data;
        }

        // ------------------------------------------------------------------
        // 3) Previsualizar sin tocar la base de datos (sólo lectura)
        // ------------------------------------------------------------------
        public static DataSet VistaPrevia()
        {
            var blocker = new LeagueBlocker(ConnStr);
            return blocker.PreviewBlockedLeagues(new List<int> { 1234, 5678 }).Data;
        }

        // ------------------------------------------------------------------
        // 4) Uso típico dentro de un controlador / servicio
        // ------------------------------------------------------------------
        public static DataSet ObtenerCuotasFiltradas(IEnumerable<int> ligasBloqueadas)
        {
            try
            {
                var blocker = new LeagueBlocker(ConnStr);
                LeagueBlockResult res = blocker.BlockLeagues(ligasBloqueadas);
                return res.Data;
            }
            catch (SqlException ex)
            {
                // Registre y reenvíe según su política de errores.
                Console.Error.WriteLine("Error SQL al bloquear ligas: " + ex.Message);
                throw;
            }
        }
    }
}

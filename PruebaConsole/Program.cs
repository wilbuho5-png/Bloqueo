using System;
using Betradar.Blocking;

namespace PruebaConsole
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("=== Proyecto de prueba: LigaBlocker (Console) ===");
            Console.WriteLine();

            // Cadena de conexión: por parámetro o variable de entorno; si no, ejemplo.
            string conn = args.Length > 0
                ? args[0]
                : Environment.GetEnvironmentVariable("LIGABLOCKER_CONN")
                  ?? "Server=localhost;Database=Betradar;Trusted_Connection=True;";

            Console.WriteLine($"Cadena de conexión: {conn}");
            Console.WriteLine("Clase disponible:   Betradar.Blocking.LeagueBlocker");
            Console.WriteLine("Métodos:            BlockLeagues(...), PreviewBlockedLeagues(...)");
            Console.WriteLine();
            Console.WriteLine("Proyecto console creado correctamente. Compilación OK.");
        }
    }
}

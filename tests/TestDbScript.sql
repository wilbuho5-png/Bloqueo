/* ============================================================
   Script de datos de prueba para LeagueBlocker (SQL Server)
   Crea una BD aislada "BetradarTest" con la estructura de
   [dbo].[Feeds] y filas de ejemplo para 2 ligas.
   Uso:  sqlcmd -S localhost -E -i TestDbScript.sql
   Luego, en los tests de integracion use la cadena:
     Server=localhost;Database=BetradarTest;Integrated Security=true;TrustServerCertificate=True
   ============================================================ */
IF DB_ID('BetradarTest') IS NULL
    CREATE DATABASE BetradarTest;
GO
USE BetradarTest;
GO

IF OBJECT_ID('dbo.Feeds', 'U') IS NOT NULL DROP TABLE dbo.Feeds;
GO

CREATE TABLE dbo.Feeds(
    NSS         int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    IdSport     int NOT NULL,
    IdLigas     int NOT NULL,
    IdLocalidad nvarchar(50) NOT NULL,
    Nombre      nvarchar(100) NULL,
    NombreLiga  nvarchar(100) NULL,
    GameId      int NULL,
    GameDate    datetime NULL,
    timestmp    datetime NULL,
    /* mercados de ejemplo (el resto se comporta igual) */
    e2Half          nvarchar(20) NULL,
    e2HalfEmpate    nvarchar(20) NULL,
    mTotalesML      nvarchar(20) NULL,
    mTotales0       nvarchar(20) NULL,
    mHandicapML     nvarchar(20) NULL,
    mCorrectscore0  nvarchar(20) NULL
);
GO

INSERT INTO dbo.Feeds
    (IdSport, IdLigas, IdLocalidad, Nombre, NombreLiga, GameId, GameDate,
     e2Half, e2HalfEmpate, mTotalesML, mTotales0, mHandicapML, mCorrectscore0)
VALUES
    (20, 1234, N'Madrid',  N'Real vs Barca',   N'La Liga',        1, GETDATE(), '1.85','3.20','1.90','2.5','2.10','7.50'),
    (20, 1234, N'Sevilla', N'Betis vs Sevilla',N'La Liga',        2, GETDATE(), '2.05','3.10','1.75','2.5','1.95','8.00'),
    (20, 5678, N'Londres', N'Arsenal vs Chelsea', N'Premier',     3, GETDATE(), '1.95','3.30','1.80','2.5','2.20','9.00'),
    (20, 9999, N'Roma',    N'Juventus vs Milan',  N'Serie A',     4, GETDATE(), '2.10','3.05','1.85','2.5','2.00','7.00');
GO

-- Comprobacion rapida tras bloquear 1234 y 5678:
-- SELECT * FROM dbo.Feeds;  -> las cuotas de esas ligas deben salir a NULL,
--                               y la liga 9999 debe conservarlas intactas.

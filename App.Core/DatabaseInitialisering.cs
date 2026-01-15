using Microsoft.Data.Sqlite;
using System.IO;

namespace App.Core;

public static class DatabaseInitialisering
{
    public static void Initialiser(string dbSti)
    {
        var mappe = Path.GetDirectoryName(dbSti);
        if (!string.IsNullOrWhiteSpace(mappe))
            Directory.CreateDirectory(mappe);

        var forbindelse = new SqliteConnectionStringBuilder
        {
            DataSource = dbSti,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        using var conn = new SqliteConnection(forbindelse);
        conn.Open();

        // Dataintegritet (vigtigt i alle DB'er, især til logs + ordrer)
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }

        // Opret tabeller (hvis de ikke findes)
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Brugere (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              brugernavn TEXT NOT NULL UNIQUE,
              kodeord_hash TEXT NOT NULL,
              rolle TEXT NOT NULL CHECK(rolle IN ('Admin','User')),
              oprettet_tid TEXT NOT NULL DEFAULT (datetime('now','localtime'))
            );

            CREATE TABLE IF NOT EXISTS Ordrer (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              oprettet_tid TEXT NOT NULL DEFAULT (datetime('now','localtime')),
              oprettet_af_bruger_id INTEGER NOT NULL,
              status TEXT NOT NULL DEFAULT 'Queued' CHECK(status IN ('Queued','Running','Done','Failed')),
              FOREIGN KEY(oprettet_af_bruger_id) REFERENCES Brugere(id)
            );

            CREATE TABLE IF NOT EXISTS OrdreLinjer (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              ordre_id INTEGER NOT NULL,
              item_klasse TEXT NOT NULL,
              maal_bin TEXT NOT NULL,
              antal INTEGER NOT NULL CHECK(antal > 0),
              prioritet INTEGER NOT NULL DEFAULT 0,
              status TEXT NOT NULL DEFAULT 'Queued' CHECK(status IN ('Queued','Running','Done','Failed')),
              FOREIGN KEY(ordre_id) REFERENCES Ordrer(id)
            );

            CREATE TABLE IF NOT EXISTS HaendelsesLog (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              tidspunkt TEXT NOT NULL DEFAULT (datetime('now','localtime')),
              bruger_id INTEGER,
              handling TEXT NOT NULL,
              detalje TEXT,
              FOREIGN KEY(bruger_id) REFERENCES Brugere(id)
            );
            """;
            cmd.ExecuteNonQuery();
        }

        // Migration: tilføj status-kolonne til OrdreLinjer hvis den mangler
        var harStatus = false;
        using (var info = conn.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(OrdreLinjer);";
            using var r = info.ExecuteReader();
            while (r.Read())
            {
                // PRAGMA table_info: (cid, name, type, notnull, dflt_value, pk)
                var colName = r.GetString(1);
                if (colName == "status")
                {
                    harStatus = true;
                    break;
                }
            }
        }

        if (!harStatus)
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = """
                ALTER TABLE OrdreLinjer
                ADD COLUMN status TEXT NOT NULL DEFAULT 'Queued';
            """;
            alter.ExecuteNonQuery();
        }
    }
}

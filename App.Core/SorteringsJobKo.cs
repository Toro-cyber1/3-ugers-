using Microsoft.Data.Sqlite;

namespace App.Core;

public record NaesteSorteringsJob(
    long OrdreId,
    long OrdreLinjeId,
    string ItemKlasse,
    string MaalBin,
    int Antal,
    int Prioritet
);

public static class SorteringsJobKo
{
    // Atomisk: hent næste QUEUED ordrelinje og reservér den (Queued -> Running)
    public static NaesteSorteringsJob? HentOgReserverNaesteSorteringsJob(string dbSti)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = dbSti }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();

        using var tx = conn.BeginTransaction();

        // 1) Find næste queued linje
        using var select = conn.CreateCommand();
        select.Transaction = tx;
        select.CommandText = """
            SELECT
                ol.ordre_id,
                ol.id,
                ol.item_klasse,
                ol.maal_bin,
                ol.antal,
                ol.prioritet
            FROM OrdreLinjer ol
            JOIN Ordrer o ON o.id = ol.ordre_id
            WHERE o.status IN ('Queued','Running')
              AND ol.status = 'Queued'
            ORDER BY ol.prioritet DESC, ol.id ASC
            LIMIT 1;
        """;

        using var r = select.ExecuteReader();
        if (!r.Read())
        {
            tx.Commit();
            return null;
        }

        var ordreId = r.GetInt64(0);
        var linjeId = r.GetInt64(1);

        var job = new NaesteSorteringsJob(
            OrdreId: ordreId,
            OrdreLinjeId: linjeId,
            ItemKlasse: r.GetString(2),
            MaalBin: r.GetString(3),
            Antal: r.GetInt32(4),
            Prioritet: r.GetInt32(5)
        );

        // 2) Reservér linjen (kun hvis den stadig er Queued)
        using var updLinje = conn.CreateCommand();
        updLinje.Transaction = tx;
        updLinje.CommandText = """
            UPDATE OrdreLinjer
            SET status = 'Running'
            WHERE id = $linjeId AND status = 'Queued';
        """;
        updLinje.Parameters.AddWithValue("$linjeId", linjeId);

        var rows = updLinje.ExecuteNonQuery();
        if (rows != 1)
        {
            // Nogen nåede at tage den før os (eller dobbelt-trigger)
            tx.Rollback();
            return null;
        }

        // 3) Sæt ordren til Running (hvis den stadig er Queued)
        using var updOrdre = conn.CreateCommand();
        updOrdre.Transaction = tx;
        updOrdre.CommandText = """
            UPDATE Ordrer
            SET status = 'Running'
            WHERE id = $ordreId AND status = 'Queued';
        """;
        updOrdre.Parameters.AddWithValue("$ordreId", ordreId);
        updOrdre.ExecuteNonQuery();

        tx.Commit();
        return job;
    }

    public static void SaetOrdreLinjeStatus(string dbSti, long ordreLinjeId, string status)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = dbSti }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE OrdreLinjer SET status = $s WHERE id = $id;";
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$id", ordreLinjeId);
        cmd.ExecuteNonQuery();
    }

    // Valgfri “ordrestyring”: sæt ordren Done hvis alle linjer er Done
    public static void FaerdiggoerOrdreHvisAlleLinjerDone(string dbSti, long ordreId)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = dbSti }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Ordrer
            SET status = 'Done'
            WHERE id = $ordreId
              AND NOT EXISTS (
                SELECT 1
                FROM OrdreLinjer
                WHERE ordre_id = $ordreId AND status != 'Done'
              );
        """;
        cmd.Parameters.AddWithValue("$ordreId", ordreId);
        cmd.ExecuteNonQuery();
    }

    // Robust "complete": Running -> Done/Failed + opdatér ordrestatus + audit log (atomisk)
    public static void FuldførJob(
        string dbSti,
        long ordreId,
        long ordreLinjeId,
        string resultat,   // "Done" eller "Failed"
        long? brugerId,
        string? detalje
    )
    {
        if (resultat != "Done" && resultat != "Failed")
            throw new ArgumentException("resultat skal være 'Done' eller 'Failed'");

        var cs = new SqliteConnectionStringBuilder { DataSource = dbSti }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();

        using var tx = conn.BeginTransaction();

        // 1) Opdatér ordrelinjen: Running -> Done/Failed
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE OrdreLinjer
                SET status = $s
                WHERE id = $linjeId AND status = 'Running';
            """;
            cmd.Parameters.AddWithValue("$s", resultat);
            cmd.Parameters.AddWithValue("$linjeId", ordreLinjeId);

            var rows = cmd.ExecuteNonQuery();
            if (rows != 1)
            {
                tx.Rollback();
                throw new InvalidOperationException("Kunne ikke fuldføre job: ordrelinjen var ikke i status 'Running'.");
            }
        }

        // 2) Hvis Failed -> sæt ordren Failed
        if (resultat == "Failed")
        {
            using var fail = conn.CreateCommand();
            fail.Transaction = tx;
            fail.CommandText = """
                UPDATE Ordrer
                SET status = 'Failed'
                WHERE id = $ordreId;
            """;
            fail.Parameters.AddWithValue("$ordreId", ordreId);
            fail.ExecuteNonQuery();
        }
        else
        {
            // 3) Hvis Done -> sæt ordren Done hvis alle linjer er Done
            using var done = conn.CreateCommand();
            done.Transaction = tx;
            done.CommandText = """
                UPDATE Ordrer
                SET status = 'Done'
                WHERE id = $ordreId
                  AND NOT EXISTS (
                    SELECT 1
                    FROM OrdreLinjer
                    WHERE ordre_id = $ordreId AND status != 'Done'
                  );
            """;
            done.Parameters.AddWithValue("$ordreId", ordreId);
            done.ExecuteNonQuery();
        }

        // 4) Audit log (sporbarhed)
        using (var log = conn.CreateCommand())
        {
            log.Transaction = tx;
            log.CommandText = """
                INSERT INTO HaendelsesLog (bruger_id, handling, detalje)
                VALUES ($brugerId, $handling, $detalje);
            """;
            log.Parameters.AddWithValue("$brugerId", (object?)brugerId ?? DBNull.Value);
            log.Parameters.AddWithValue("$handling", resultat == "Done" ? "JOB_DONE" : "JOB_FAILED");
            log.Parameters.AddWithValue("$detalje", (object?)detalje ?? DBNull.Value);
            log.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public static List<NaesteSorteringsJob> HentJobListe(string dbSti, int max = 50)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = dbSti }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                ol.ordre_id,
                ol.id,
                ol.item_klasse,
                ol.maal_bin,
                ol.antal,
                ol.prioritet
            FROM OrdreLinjer ol
            JOIN Ordrer o ON o.id = ol.ordre_id
            WHERE o.status IN ('Queued','Running')
            ORDER BY ol.status DESC, ol.prioritet DESC, ol.id ASC
            LIMIT $max;
        """;
        cmd.Parameters.AddWithValue("$max", max);

        var list = new List<NaesteSorteringsJob>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new NaesteSorteringsJob(
                OrdreId: r.GetInt64(0),
                OrdreLinjeId: r.GetInt64(1),
                ItemKlasse: r.GetString(2),
                MaalBin: r.GetString(3),
                Antal: r.GetInt32(4),
                Prioritet: r.GetInt32(5)
            ));
        }

        return list;
    }
}

using Microsoft.Data.Sqlite;

namespace App.Core;

public record OrdreLinjeInput(string ItemKlasse, string MaalBin, int Antal, int Prioritet = 0);

public static class OrdreOprettelse
{
    public static long OpretOrdreMedLinjer(string dbSti, int oprettetAfBrugerId, IEnumerable<OrdreLinjeInput> linjer)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = dbSti }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();

        using var tx = conn.BeginTransaction();

        // Opret ordre
        long ordreId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                                  INSERT INTO Ordrer (oprettet_af_bruger_id, status)
                                  VALUES ($brugerId, 'Queued');
                                  SELECT last_insert_rowid();
                              """;
            cmd.Parameters.AddWithValue("$brugerId", oprettetAfBrugerId);
            ordreId = (long)cmd.ExecuteScalar()!;
        }

        // Opret ordrelinjer
        foreach (var l in linjer)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                                  INSERT INTO OrdreLinjer (ordre_id, item_klasse, maal_bin, antal, prioritet)
                                  VALUES ($ordreId, $klasse, $bin, $antal, $prio);
                              """;
            cmd.Parameters.AddWithValue("$ordreId", ordreId);
            cmd.Parameters.AddWithValue("$klasse", l.ItemKlasse);
            cmd.Parameters.AddWithValue("$bin", l.MaalBin);
            cmd.Parameters.AddWithValue("$antal", l.Antal);
            cmd.Parameters.AddWithValue("$prio", l.Prioritet);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return ordreId;
    }
}
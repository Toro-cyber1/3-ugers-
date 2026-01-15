using Microsoft.Data.Sqlite;

namespace App.Core;

public static class HaendelsesLogger
{
    public static void Log(string dbSti, int? brugerId, string handling, string? detalje = null)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = dbSti }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                              INSERT INTO HaendelsesLog (bruger_id, handling, detalje)
                              VALUES ($brugerId, $handling, $detalje);
                          """;

        cmd.Parameters.AddWithValue("$brugerId", (object?)brugerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$handling", handling);
        cmd.Parameters.AddWithValue("$detalje", (object?)detalje ?? DBNull.Value);

        cmd.ExecuteNonQuery();
    }
}
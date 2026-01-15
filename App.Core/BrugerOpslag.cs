using Microsoft.Data.Sqlite;

namespace App.Core;

public static class BrugerOpslag
{
    public static int? FindBrugerId(string dbSti, string brugernavn)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = dbSti }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM Brugere WHERE brugernavn = $u LIMIT 1;";
        cmd.Parameters.AddWithValue("$u", brugernavn);

        var result = cmd.ExecuteScalar();
        if (result is null || result is DBNull) return null;

        return Convert.ToInt32(result);
    }
}
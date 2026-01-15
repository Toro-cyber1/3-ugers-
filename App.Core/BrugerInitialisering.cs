using Microsoft.Data.Sqlite;

namespace App.Core;

public static class BrugerInitialisering
{
    public static void SikrAdmin(string dbSti, string brugernavn, string kodeord)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = dbSti }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();

        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(1) FROM Brugere WHERE brugernavn = $u;";
        check.Parameters.AddWithValue("$u", brugernavn);

        var findes = (long)check.ExecuteScalar()! > 0;
        if (findes) return;

        var hash = BCrypt.Net.BCrypt.HashPassword(kodeord);

        using var insert = conn.CreateCommand();
        insert.CommandText = """
                                 INSERT INTO Brugere (brugernavn, kodeord_hash, rolle)
                                 VALUES ($u, $h, 'Admin');
                             """;
        insert.Parameters.AddWithValue("$u", brugernavn);
        insert.Parameters.AddWithValue("$h", hash);
        insert.ExecuteNonQuery();
    }
}
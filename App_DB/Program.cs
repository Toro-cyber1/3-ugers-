using App.Core;
using Microsoft.Data.Sqlite;
using System.IO;

var dbSti = Path.Combine(AppContext.BaseDirectory, "App_Data", "app.db");

DatabaseInitialisering.Initialiser(dbSti);
BrugerInitialisering.SikrAdmin(dbSti, "admin", "Gruppe4");


var adminId = BrugerOpslag.FindBrugerId(dbSti, "admin") ?? 0;

// Opret kun testordre hvis der ikke findes aktive ordrer i forvejen
if (!HarAktiveOrdrer(dbSti))
{
    var ordreId = OrdreOprettelse.OpretOrdreMedLinjer(
        dbSti,
        oprettetAfBrugerId: adminId,
        linjer: new[]
        {
            new OrdreLinjeInput(ItemKlasse: "RED",  MaalBin: "VIDERESALG",         Antal: 5, Prioritet: 1),
            new OrdreLinjeInput(ItemKlasse: "BLUE", MaalBin: "MATERIALEGENANVEND", Antal: 5, Prioritet: 0),
        }
    );

    Console.WriteLine($"Oprettet ordre: {ordreId}");
}
else
{
    Console.WriteLine("Springer oprettelse af testordre over (der findes allerede aktive ordrer).");
}

HaendelsesLogger.Log(dbSti, adminId, "START_SORTERING", "Job startet fra GUI");

// Hent + reserver næste job (Queued -> Running atomisk)
var job = SorteringsJobKo.HentOgReserverNaesteSorteringsJob(dbSti);

if (job is null)
{
    Console.WriteLine("Ingen jobs i køen.");
}
else
{
    Console.WriteLine($"Næste job: ordre={job.OrdreId}, linje={job.OrdreLinjeId}, klasse={job.ItemKlasse}, bin={job.MaalBin}, antal={job.Antal}");

    // Vi har kun én robot – robotten er altid den samme.
    // “Robot handling” er bare hvilken handling robotten skal udføre.
    var robotHandling = job.MaalBin switch
    {
        "VIDERESALG" => "PLACER_I_BIN_A",
        "MATERIALEGENANVEND" => "PLACER_I_BIN_B",
        _ => "STOP_OG_ALARM"
    };

    Console.WriteLine($"Robot handling: {robotHandling}");

    HaendelsesLogger.Log(dbSti, adminId, "JOB_HENTET",
        $"ordre={job.OrdreId}, linje={job.OrdreLinjeId}, robot={robotHandling}");

    // Simulér at robotten har udført jobbet OK -> markér Done
    SorteringsJobKo.SaetOrdreLinjeStatus(dbSti, job.OrdreLinjeId, "Done");
    HaendelsesLogger.Log(dbSti, adminId, "JOB_DONE",
        $"ordre={job.OrdreId}, linje={job.OrdreLinjeId}");

    // Valgfrit: sæt ordren Done hvis alle linjer i ordren er Done
    SorteringsJobKo.FaerdiggoerOrdreHvisAlleLinjerDone(dbSti, job.OrdreId);
}

VisSenesteLog(dbSti);

Console.WriteLine("Database er klar, og admin-bruger er sikret.");

static bool HarAktiveOrdrer(string dbSti)
{
    var cs = new SqliteConnectionStringBuilder { DataSource = dbSti }.ToString();
    using var conn = new SqliteConnection(cs);
    conn.Open();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
                          SELECT COUNT(*)
                          FROM OrdreLinjer ol
                          JOIN Ordrer o ON o.id = ol.ordre_id
                          WHERE o.status IN ('Queued','Running')
                            AND ol.status IN ('Queued','Running');
                      """;

    var count = (long)cmd.ExecuteScalar()!;
    return count > 0;
}



static void VisSenesteLog(string dbSti)
{
    var cs = new SqliteConnectionStringBuilder { DataSource = dbSti }.ToString();
    using var conn = new SqliteConnection(cs);
    conn.Open();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT tidspunkt, bruger_id, handling, detalje
        FROM HaendelsesLog
        ORDER BY id DESC
        LIMIT 1;
        """;

    using var reader = cmd.ExecuteReader();
    if (reader.Read())
    {
        Console.WriteLine(
            $"Seneste log: {reader.GetString(0)} | bruger_id={reader.GetValue(1)} | {reader.GetString(2)} | {reader.GetValue(3)}"
        );
    }
}

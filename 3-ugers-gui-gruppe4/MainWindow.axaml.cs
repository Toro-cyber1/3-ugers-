using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using App.Core;
using Microsoft.Data.Sqlite;

namespace _3_ugers_gui_gruppe4;

public partial class MainWindow : Window
{
    private NaesteSorteringsJob? _senesteJob;

    private const string RobotIp = "172.20.254.208"; 
    private const int RobotPort = 30002;             

    public MainWindow()
    {
        InitializeComponent();
        
        ResetRunningToQueuedButton.Click += ResetRunningToQueued_Click;
        
        Log($"DB: {CoreContext.DbSti}");
        Log($"AdminId: {CoreContext.AdminId}");

        // --- ROBOTSCRIPT STARTUP TEST (fjern senere hvis du vil) ---
        try
        {
            var test = LoadRobotScript("blaa_26.script");
            Log($"Loaded blaa_26.script: {test.Length} chars");
        }
        catch (Exception ex)
        {
            Log($"SCRIPT LOAD FAIL: {ex.Message}");
        }
        // ----------------------------------------------------------

        RefreshJobs();
    }

    private int GetAdminIdAsInt()
    {
        return checked((int)CoreContext.AdminId);
    }

    private void RefreshJobs()
    {
        try
        {
            JobsGrid.ItemsSource = SorteringsJobKo.HentJobListe(CoreContext.DbSti, max: 50);
            Log("Jobliste opdateret.");
        }
        catch (Exception ex)
        {
            Log($"FEJL (RefreshJobs): {ex.Message}");
        }
    }

    private void OpretTestordre_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (HarAktiveOrdrer(CoreContext.DbSti))
            {
                Log("Springer testordre over (der findes allerede aktive ordrer).");
                RefreshJobs();
                return;
            }

            var adminId = GetAdminIdAsInt();

            var ordreId = OrdreOprettelse.OpretOrdreMedLinjer(
                dbSti: CoreContext.DbSti,
                oprettetAfBrugerId: adminId,
                linjer: new[]
                {
                    new OrdreLinjeInput(ItemKlasse: "RED",  MaalBin: "VIDERESALG",         Antal: 5, Prioritet: 1),
                    new OrdreLinjeInput(ItemKlasse: "BLUE", MaalBin: "MATERIALEGENANVEND", Antal: 5, Prioritet: 0),
                }
            );

            HaendelsesLogger.Log(CoreContext.DbSti, adminId, "TESTORDRE_OPRETTET", $"ordre={ordreId}");
            Log($"Oprettet testordre: {ordreId}");

            RefreshJobs();
        }
        catch (Exception ex)
        {
            Log($"FEJL (OpretTestordre): {ex.Message}");
        }
    }

    // ===== FÆLLES ROBOT SEND =====
    private async Task<bool> SendToRobotAsync(string scriptFile, string script)
    {
        try
        {
            Log($"Robot: connecting {RobotIp}:{RobotPort} ...");
            var robot = new UrRobotClient(RobotIp, RobotPort);

            // kræver den opdaterede UrRobotClient med timeout overload
            await robot.SendScriptAsync(script, TimeSpan.FromSeconds(5));

            Log($"Robot: SEND OK ({scriptFile})");
            return true;
        }
        catch (Exception ex)
        {
            Log($"Robot: SEND FAIL ({scriptFile}) -> {ex.Message}");
            return false;
        }
    }
    // ============================

    private async void HentNaesteJob_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var job = SorteringsJobKo.HentOgReserverNaesteSorteringsJob(CoreContext.DbSti);

            if (job is null)
            {
                Log("Ingen jobs i køen.");
                _senesteJob = null;
                RefreshJobs();
                return;
            }

            _senesteJob = job;

            Log($"Job reserveret: ordre={job.OrdreId}, linje={job.OrdreLinjeId}, klasse={job.ItemKlasse}, bin={job.MaalBin}, antal={job.Antal}, prio={job.Prioritet}");

            var adminId = GetAdminIdAsInt();
            HaendelsesLogger.Log(CoreContext.DbSti, adminId, "JOB_HENTET", $"ordre={job.OrdreId}, linje={job.OrdreLinjeId}");

            // 1) Vælg + load script
            var scriptFile = ScriptFileForItemKlasse(job.ItemKlasse);

            var raw = LoadRobotScript(scriptFile);

            // 1) sørg for at funktionen bliver kaldt
            var script = EnsureScriptRuns(raw);

            // 2) prepend en popup så du kan se på teach pendant at robotten faktisk starter scriptet
            script = $"popup(\"Starting {scriptFile}\", title=\"PC\", warning=False)\n" + script;

            Log($"RobotScript valgt: {scriptFile} ({raw.Length} chars) + popup + auto-call");

            var ok = await SendToRobotAsync(scriptFile, script);



            if (!ok)
            {
                // Fail fast: undgå stuck Running
                SorteringsJobKo.FuldførJob(
                    dbSti: CoreContext.DbSti,
                    ordreId: job.OrdreId,
                    ordreLinjeId: job.OrdreLinjeId,
                    resultat: "Failed",
                    brugerId: adminId,
                    detalje: $"Robot send fejlede for {scriptFile}"
                );

                Log($"JOB_FAILED pga robot-send: ordre={job.OrdreId}, linje={job.OrdreLinjeId}");
                _senesteJob = null;
            }
            else
            {
                // Indtil I har robot-feedback: manuelt Done
                Log("Robot kører nu jobbet. Klik 'Markér seneste job Done' når jobbet er udført.");
            }

            RefreshJobs();
        }
        catch (Exception ex)
        {
            Log($"FEJL (HentNaesteJob): {ex.Message}");
        }
    }

    private async void TestRobotMove_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Log("TEST: TestRobotMove_Click fired");

        var scriptFile = "INLINE_TEST_MOVE_DEF";

        var script = """
                     def pc_test_move():
                       textmsg("PC def running - about to move")
                       sleep(0.2)
                       p0 = get_actual_tcp_pose()
                       p1 = pose_trans(p0, p[0,0,0.005,0,0,0])  # 5 mm
                       movel(p1, a=0.2, v=0.02)
                       textmsg("PC def done")
                     end

                     pc_test_move()
                     """;


        await SendToRobotAsync(scriptFile, script);
    }


    private void MarkJobDone_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_senesteJob is null)
            {
                Log("Ingen 'seneste job' at markere. Klik 'Hent næste job' først.");
                return;
            }

            var adminId = GetAdminIdAsInt();

            SorteringsJobKo.FuldførJob(
                dbSti: CoreContext.DbSti,
                ordreId: _senesteJob.OrdreId,
                ordreLinjeId: _senesteJob.OrdreLinjeId,
                resultat: "Done",
                brugerId: adminId,
                detalje: $"ordre={_senesteJob.OrdreId}, linje={_senesteJob.OrdreLinjeId}"
            );

            Log($"JOB_DONE: ordre={_senesteJob.OrdreId}, linje={_senesteJob.OrdreLinjeId}");
            _senesteJob = null;

            RefreshJobs();
        }
        catch (Exception ex)
        {
            Log($"FEJL (MarkJobDone): {ex.Message}");
        }
    }

    private void RefreshJobs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => RefreshJobs();
    
    private void ResetRunningToQueued_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Log("RESET: Click fired"); // <- hvis du ikke ser denne, er knappen ikke wired

        try
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = CoreContext.DbSti }.ToString()
            );
            conn.Open();

            using var tx = conn.BeginTransaction();

            var linjerRunning = 0;
            var ordrerRunning = 0;

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE OrdreLinjer SET status='Queued' WHERE status='Running';";
                linjerRunning = cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE Ordrer SET status='Queued' WHERE status='Running';";
                ordrerRunning = cmd.ExecuteNonQuery();
            }

            tx.Commit();

            Log($"RESET: Done. OrdreLinjer reset={linjerRunning}, Ordrer reset={ordrerRunning}");
            RefreshJobs();
        }
        catch (Exception ex)
        {
            Log($"RESET: FAIL -> {ex.Message}");
        }
    }
    
    private static bool HarAktiveOrdrer(string dbSti)
    {
        using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = dbSti }.ToString()
        );
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Ordrer WHERE status IN ('Queued','Running');";
        var count = (long)cmd.ExecuteScalar()!;
        return count > 0;
    }
    
    private static string EnsureScriptRuns(string rawScript)
    {
        var m = Regex.Match(rawScript, @"^\s*def\s+([A-Za-z_]\w*)\s*\(", RegexOptions.Multiline);
        if (!m.Success)
            return rawScript;

        var fn = m.Groups[1].Value;

        if (Regex.IsMatch(rawScript, $@"(?m)^\s*{Regex.Escape(fn)}\s*\(\s*\)\s*$"))
            return rawScript;

        return rawScript.TrimEnd() + "\n\n" + fn + "()\n";
    }

    
    // ================= ROBOT SCRIPTS =================

    private static string LoadRobotScript(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RobotScripts", fileName);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Robot script not found: {path}");

        return File.ReadAllText(path);
    }

    private static string ScriptFileForItemKlasse(string itemKlasse)
    {
        var key = (itemKlasse ?? "").Trim().ToUpperInvariant();

        return key switch
        {
            "BLUE" => "blaa_26.script",
            "GREEN" => "groen_26.script",
            "RED" => "roed_26.script",
            _ => throw new ArgumentException($"Ukendt ItemKlasse: '{itemKlasse}' (kan ikke mappe til script)")
        };
    }

    // =================================================

    private void Log(string msg)
    {
        OutputBox.Text += $"{DateTime.Now:HH:mm:ss} | {msg}\n";
        OutputBox.CaretIndex = OutputBox.Text.Length;
    }
}


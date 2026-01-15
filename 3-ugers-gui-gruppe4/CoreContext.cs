using System;
using System.IO;
using App.Core;

namespace _3_ugers_gui_gruppe4;

public static class CoreContext
{
    public static string DbSti { get; } =
        Path.Combine(AppContext.BaseDirectory, "App_Data", "app.db");

    public static long AdminId { get; private set; }

    public static void Init()
    {
        DatabaseInitialisering.Initialiser(DbSti);

        // Midlertidigt: auto-admin til demo
        BrugerInitialisering.SikrAdmin(DbSti, "admin", "Gruppe4");
        AdminId = BrugerOpslag.FindBrugerId(DbSti, "admin") ?? 0;
    }
}
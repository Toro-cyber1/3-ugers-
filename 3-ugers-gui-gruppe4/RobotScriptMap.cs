using System;

namespace _3_ugers_gui_gruppe4;

public static class RobotScriptMap
{
    public static string FileForItemKlasse(string itemKlasse)
    {
        var key = (itemKlasse ?? "").Trim().ToLowerInvariant();

        return key switch
        {
            "blaa" or "blue"  => "blaa_26.script",
            "groen" or "green"=> "groen_26.script",
            "roed" or "red"   => "roed_26.script",
            _ => throw new ArgumentException($"Ukendt item_klasse: '{itemKlasse}'")
        };
    }
}
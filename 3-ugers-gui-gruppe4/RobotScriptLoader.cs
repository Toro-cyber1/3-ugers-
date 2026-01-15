using System;
using System.IO;

namespace _3_ugers_gui_gruppe4;

public static class RobotScriptLoader
{
    public static string Load(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RobotScripts", fileName);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Robot script not found: {path}");

        return File.ReadAllText(path);
    }
}
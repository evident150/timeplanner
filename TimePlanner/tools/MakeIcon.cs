using System;
using TimePlanner.Core;

public class MakeIcon
{
    public static void Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "assets/app.ico";
        if (args.Length > 1) Theme.SetAccent(args[1]);
        AppIcon.SaveIco(path);
        Console.WriteLine("icon -> " + path);
    }
}

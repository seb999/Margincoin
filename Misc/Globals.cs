using System.Collections.Generic;
using MarginCoin.Class;

public static class Globals
{
    public static Dictionary<string, bool> onHold = new Dictionary<string, bool>();

    public static bool isProd = false;

    public static bool isTradingOpen = false;

    public static bool swallowOneOrder = false;
}


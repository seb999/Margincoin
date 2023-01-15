using System.Collections.Generic;
using MarginCoin.Class;

public static class Globals
{
    public static Dictionary<string, bool> onHold = new Dictionary<string, bool>();

    // use Binance prod server or Binance test server
    public static bool isProd = false;

    // start / stop auto trading
    public static bool isTradingOpen = false;

    // execute binance orders or not
    public static bool onAir = false;

    // To debug and force openeing a trade 
    public static bool swallowOneOrder = false;

    public static bool fullSymbolList;
}


using System.Collections.Generic;
using MarginCoin.Class;
using MarginCoin.Model;

public static class Global
{
    public static List<Symbol> SymbolWeTrade = new List<Symbol>();
    public static List<Symbol> SymbolBaseList = new List<Symbol>();
    public static Dictionary<string, bool> onHold = new Dictionary<string, bool>();
    // use Binance prod server or Binance test server
    public static bool isProd = false;
    // start / stop auto trading
    public static bool isTradingOpen = false;
    // To debug and force openeing a trade 
    public static bool swallowOneOrder = false;
    public static bool fullSymbolList;
    internal static bool syncBinanceSymbol;
    internal static List<List<Candle>> candleMatrix = new List<List<Candle>>();
    internal static int? nbrOfSymbol;
    public static string interval;
    internal static double stopLossPercentage;
    internal static double takeProfitPercentage;
}
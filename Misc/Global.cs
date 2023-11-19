using System.Collections.Generic;
using MarginCoin.Class;
using MarginCoin.Model;

public static class Global
{
    //List to be shared during trading
    internal static List<Symbol> SymbolWeTrade = new List<Symbol>();
    internal static List<Symbol> SymbolBaseList = new List<Symbol>();
    internal static Dictionary<string, bool> onHold = new Dictionary<string, bool>();
    internal static List<List<Candle>> candleMatrix = new List<List<Candle>>();
    
    //Bool
    internal static bool isProd = false;
    internal static bool syncBinanceSymbol;
    internal static bool isTradingOpen = false;
    public static bool testBuyLimit = false;
    
    //Others
    internal static int? nbrOfSymbol;
    internal static string interval;
    internal static double stopLossPercentage;
    internal static double takeProfitPercentage;
}
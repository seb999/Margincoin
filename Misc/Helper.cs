using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using MarginCoin.ClassTransfer;

namespace MarginCoin.Misc
{
    public static class Helper
    {
        public static void ShortenSymbol(ref List<SymbolTransfer> coinList)
        {
            foreach (var item in coinList)
            {
              //  item.symbolShort = item.symbol;

                item.symbolShort = item.symbol.Substring(0, item.symbol.Length - 4);
                item.symbolBase = item.symbol.Substring(item.symbol.Length - 4);
                item.iconUrl = item.symbol.Substring(0, item.symbol.Length - 4);
            }
        }

        public static void ShortenSymbol(ref SymbolTransfer symbol)
        {

            symbol.symbolShort = symbol.symbol;

            if (symbol.symbol.Substring(symbol.symbol.Length - 4) == "USD")
            {
                symbol.symbolShort = symbol.symbol.Substring(0, symbol.symbol.Length - 4) + "/" + "USD";
            }
        }
    }
}
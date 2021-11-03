
using System;
using System.Collections.Generic;

namespace MarginCoin.Class
{
    public class SymbolTransfer
    {
        public string symbol { get; set; }
        public string symbolShort { get; set; }
        public string symbolBase { get; set; }
        public string iconUrl { get; set; }
        public double priceChange { get; set; }
        public double priceChangePercent { get; set; }
        public double prevClosePrice { get; set; }
        public double lastPrice { get; set; }
        public double lastQty { get; set; }
        public double bidPrice { get; set; }
        public double bidQty { get; set; }
        public double askPrice { get; set; }
        public double askQty { get; set; }
        public double openPrice { get; set; }
        public double highPrice { get; set; }
        public double lowPrice { get; set; }
        public double volume { get; set; }
        public double quoteVolume { get; set; }

         //Indicators
        public double rsi { get; set; }
        public double macd { get; set; }
        public double macdSign { get; set; }
        public double macdHist { get; set; }

    }
}

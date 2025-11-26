using System;
using MarginCoin.Class;
using System.Collections.Generic;
using MarginCoin.Misc;

namespace MarginCoin.Service
{
    public interface IBinanceService
    {
        string Interval { get; set; }
        string Limit { get; set; } 

        public List<BinanceAsset> Asset();
        public BinanceAccount Account();
        public List<BinancePrice> GetSymbolPrice ();
        public void GetCandles(string symbol, ref List<List<Candle>> candleMatrix);
        public BinanceOrder OrderStatus(string symbol, double orderId);
        public BinanceOrder CancelSymbolOrder(string symbol); 
        public BinanceOrder CancelOrder(string symbol, double orderId);
        public BinanceOrder BuyMarket(string symbol, double quoteQty);
        public BinanceOrder SellMarket(string symbol, double qty);
        public BinanceOrder BuyLimit(string symbol, double qty, double price, MyEnum.TimeInForce timeInForce);
        public BinanceOrder SellLimit(string symbol, double qty, double price, MyEnum.TimeInForce timeInForce);

        public BinanceTicker Ticker(string symbol);

    }
}
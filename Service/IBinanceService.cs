using System;
using MarginCoin.Class;
using System.Collections.Generic;
using MarginCoin.Misc;

namespace MarginCoin.Service
{
    public interface IBinanceService
    {
        const string testPublicKey = "HsKWfKtktmw07gqsCyK1TJThULUnAivnFxF13vFUZf4WjLJXsbwmaPOIgw5rNAuQ";  //for https://testnet.binance.vision/
        const string testSecretKey = "ncSzN6J4Efh8Xb53e1uYkuHCw9VFAemUKjCEPdwY5WtdbMJOAEzEIuP5qMrjKewX";
        const string prodPublicKey = "gIDNZ9OsVIUbvFEuLgOhZ3XoQRnwrJ8krkp3TAR2dxQxwYErmKC6GOsMy50LYGWy";
        static string prodSecretKey = Environment.GetEnvironmentVariable("BSK");

        public static string secretKey = "";
        public static string publicKey = "";
        public static string host = "";
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
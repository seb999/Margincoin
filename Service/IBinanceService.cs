using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using MarginCoin.Class;
using System.Collections.Generic;
using MarginCoin.Controllers;
using MarginCoin.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace MarginCoin.Misc
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


        public List<BinanceAsset> Asset(ref System.Net.HttpStatusCode httpStatusCode);

        public BinanceAccount Account(ref System.Net.HttpStatusCode httpStatusCode);

        public BinanceOrder OrderStatus(string symbol, double orderId);

        public BinanceOrder BuyMarket(string symbol, double quoteQty, ref System.Net.HttpStatusCode httpStatusCode);

        ///Buy as much possible for quoteQty USDT specified
        public BinanceOrder BuyLimit(string symbol, double quoteQty, MyEnum.TimeInForce timeInForce, ref System.Net.HttpStatusCode httpStatusCode);

        ///Sell a quantity of the symbol
        public BinanceOrder SellMarket(string symbol, double qty, ref System.Net.HttpStatusCode httpStatusCode);

        // public static BinanceOrder BuyLimit(string symbol, double quantity, Enum.TimeInForce timeInForce)
        // {
        //     string stringQuantity = quantity.ToString().Replace(",", ".");
        //     System.Net.HttpStatusCode httpStatusCode = System.Net.HttpStatusCode.NoContent;
        //     try
        //     {
        //         SetEnv(ref secretKey, ref publicKey, ref host);
        //         string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&quantity={stringQuantity}&timeInForce={timeInForce.ToString()}&side=BUY&type=LIMIT&recvWindow=60000";
        //         string signature = GetSignature(parameters, secretKey);
        //         string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

        //         if(!Globals.isProd)
        //         { 
        //             apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";
        //         }
        //         return HttpHelper.PostApiData<BinanceOrder>(new Uri(apiUrl), publicKey, new StringContent("", Encoding.UTF8, "application/json"), ref httpStatusCode);

        //     }
        //     catch (System.Exception e)
        //     { 
        //         Console.WriteLine(e);
        //         return null;
        //     }
        // }

        public void SellLimit(string symbol, double quantity, MyEnum.TimeInForce timeInForce, ref System.Net.HttpStatusCode httpStatusCode);
    }
}
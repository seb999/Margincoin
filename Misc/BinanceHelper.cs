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
    public static class BinanceHelper123
    {
        const string testPublicKey = "HsKWfKtktmw07gqsCyK1TJThULUnAivnFxF13vFUZf4WjLJXsbwmaPOIgw5rNAuQ";  //for https://testnet.binance.vision/
        const string testSecretKey = "ncSzN6J4Efh8Xb53e1uYkuHCw9VFAemUKjCEPdwY5WtdbMJOAEzEIuP5qMrjKewX";
        const string prodPublicKey = "gIDNZ9OsVIUbvFEuLgOhZ3XoQRnwrJ8krkp3TAR2dxQxwYErmKC6GOsMy50LYGWy";
        static string prodSecretKey = Environment.GetEnvironmentVariable("BSK");

        public static string secretKey ="";
        public static string publicKey =""; 
        public static string host  ="";

        public static BinanceAccount Account(ref System.Net.HttpStatusCode httpStatusCode)
        {
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);

                string parameters = $"timestamp={ServerTime(publicKey)}&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/account?{parameters}&signature={signature}";

                if(!Globals.isProd)
                { 
                    apiUrl = $"{host}/api/v3/account?{parameters}&signature={signature}";
                }

                var result = HttpHelper.GetApiData<BinanceAccount>(new Uri(apiUrl), publicKey, ref httpStatusCode);
                if (result != null)
                {
                    return result;
                }
                else
                {
                    return null;
                }
            }
            catch (System.Exception e)
            {
                Console.WriteLine(e);
                return null;
            }
        }


        public static BinanceOrder OrderStatus(string symbol, double orderId)
        {
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&orderId={orderId}&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

                if(!Globals.isProd)
                { 
                    apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";
                }
                var ttt = HttpHelper.GetApiData<BinanceOrder>(new Uri(apiUrl), publicKey);
                return ttt;
            }
            catch (System.Exception e)
            { 
                Console.WriteLine(e);
                return null;
            }
        }
        
        public static BinanceOrder BuyMarket(string symbol, double quoteQty, ref System.Net.HttpStatusCode httpStatusCode)
        {
            string stringQty= quoteQty.ToString().Replace(",", ".");
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&quoteOrderQty={stringQty}&side=BUY&type=MARKET&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

                if(!Globals.isProd)
                { 
                    apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";
                }
               
                return HttpHelper.PostApiData<BinanceOrder>(new Uri(apiUrl), publicKey, new StringContent("", Encoding.UTF8, "application/json"), ref httpStatusCode);
            }
            catch (System.Exception e)
            { 
                Console.WriteLine(e);
                return null;
            }
        }

        ///Buy as much possible for quoteQty USDT specified
        public static BinanceOrder BuyLimit(string symbol, double quoteQty, MyEnum.TimeInForce timeInForce, ref System.Net.HttpStatusCode httpStatusCode)
        {
            string stringQuoteQty = quoteQty.ToString().Replace(",", ".");
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&quoteOrderQty={stringQuoteQty}&timeInForce={timeInForce.ToString()}&side=BUY&type=LIMIT&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

                if(!Globals.isProd)
                { 
                    apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";
                }
                return HttpHelper.PostApiData<BinanceOrder>(new Uri(apiUrl), publicKey, new StringContent("", Encoding.UTF8, "application/json"), ref httpStatusCode); 
            }
            catch (System.Exception e)
            { 
                Console.WriteLine(e);
                return null;
            }
        }

        ///Sell a quantity of the symbol
        public static BinanceOrder SellMarket(string symbol, double qty, ref System.Net.HttpStatusCode httpStatusCode)
        {
            string stringQuantity = qty.ToString().Replace(",", ".");
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&quantity={stringQuantity}&side=SELL&type=MARKET&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

                if(!Globals.isProd)
                { 
                    apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";
                }
                return  HttpHelper.PostApiData<BinanceOrder>(new Uri(apiUrl), publicKey, new StringContent("", Encoding.UTF8, "application/json"), ref httpStatusCode);
            }
            catch (System.Exception e)
            { 
                Console.WriteLine(e);
                return null;
            }
        }

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

        public static void SellLimit(string symbol, double quantity, MyEnum.TimeInForce timeInForce, ref System.Net.HttpStatusCode httpStatusCode)
        {
            string stringQuantity = quantity.ToString().Replace(",", ".");
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&quantity={stringQuantity}&timeInForce={timeInForce.ToString()}&side=SELL&type=LIMIT&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

                if(!Globals.isProd)
                { 
                    apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";
                }
                BinanceOrder transaction = HttpHelper.PostApiData<BinanceOrder>(new Uri(apiUrl), publicKey, new StringContent("", Encoding.UTF8, "application/json"), ref httpStatusCode);
                Console.WriteLine("http request completed");
                // await _hub.Clients.All.SendAsync("transferExecuted", "done");
            }
            catch (System.Exception e)
            { 
                Console.WriteLine(e);
            }
        }

        #region helper

        private static void SetEnv(ref string secretKey, ref string publicKey, ref string host)
        {
            if (Globals.isProd)
            {
                secretKey = prodSecretKey;
                publicKey = prodPublicKey;
                host = "https://api3.binance.com";
            }
            else
            {
                secretKey = testSecretKey;
                publicKey = testPublicKey;
                host = "https://testnet.binance.vision";
            }
        }

        private static long ServerTime(string apiKey)
        {
            string apiUrl;
            if (Globals.isProd)
            {
                apiUrl = string.Format("https://api.binance.com/api/v3/time");
            }
            else
            {
                apiUrl = string.Format("https://testnet.binance.vision/api/v3/time");
            }
            BinanceTimeStamp result = HttpHelper.GetApiData<BinanceTimeStamp>(new Uri(apiUrl), apiKey);
            return result.ServerTime;

        }

        private static string GetSignature(string totalParams, string secretKey)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(secretKey);
            byte[] messageBytes = Encoding.UTF8.GetBytes(totalParams);
            HMACSHA256 hmacsha256 = new HMACSHA256(keyBytes);
            byte[] bytes = hmacsha256.ComputeHash(messageBytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        private static long GetTimestamp()
        {
            return new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();
        }

        #endregion
    }
}
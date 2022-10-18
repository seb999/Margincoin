using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using MarginCoin.Class;
using System.Collections.Generic;

namespace MarginCoin.Misc
{
    public static class BinanceHelper
    {
        const string testPublicKey = "HsKWfKtktmw07gqsCyK1TJThULUnAivnFxF13vFUZf4WjLJXsbwmaPOIgw5rNAuQ";  //for https://testnet.binance.vision/
        const string testSecretKey = "ncSzN6J4Efh8Xb53e1uYkuHCw9VFAemUKjCEPdwY5WtdbMJOAEzEIuP5qMrjKewX";
        const string prodPublicKey = "gIDNZ9OsVIUbvFEuLgOhZ3XoQRnwrJ8krkp3TAR2dxQxwYErmKC6GOsMy50LYGWy";
        static string prodSecretKey = Environment.GetEnvironmentVariable("BSK");
        public static List<CryptoAsset> Asset(ref System.Net.HttpStatusCode httpStatusCode)
        {
            try
            {
                string secretKey = "";
                string publicKey = "";
                string host = "";
                SetEnv(ref secretKey, ref publicKey, ref host);

                string parameters = $"timestamp={ServerTime(publicKey)}&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/sapi/v3/asset/getUserAsset?{parameters}&signature={signature}";

                var result = HttpHelper.PostApiData<List<CryptoAsset>>(new Uri(apiUrl), publicKey, new StringContent("", Encoding.UTF8, "application/json"), ref httpStatusCode);
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

        public static async void Buy(string symbol, double amount)
        {
            string stringAmount = amount.ToString().Replace(",", ".");
            System.Net.HttpStatusCode httpStatusCode = System.Net.HttpStatusCode.NoContent;
            try
            {
                string secretKey = "";
                string publicKey = "";
                string host = "";
                SetEnv(ref secretKey, ref publicKey, ref host);

                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&quoteOrderQty={stringAmount}&side=BUY&type=MARKET&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order/test?{parameters}&signature={signature}";

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
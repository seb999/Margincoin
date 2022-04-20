using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using MarginCoin.Class;

namespace MarginCoin.Misc
{
    public static class BinanceHelper
    {
        public static async void Buy(string symbol, double amount)
        {
            string stringAmount = amount.ToString().Replace(",", ".");
            try
            {
                string secretKey = Environment.GetEnvironmentVariable("BSK");
                string apiKey = "gIDNZ9OsVIUbvFEuLgOhZ3XoQRnwrJ8krkp3TAR2dxQxwYErmKC6GOsMy50LYGWy";

                string parameters = $"timestamp={ServerTime(apiKey)}&symbol={symbol}&quoteOrderQty={stringAmount}&side=BUY&type=MARKET&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"https://api3.binance.com/api/v3/order/test?{parameters}&signature={signature}";

                BinanceOrder transaction = HttpHelper.PostApiData<BinanceOrder>(new Uri(apiUrl), apiKey, new StringContent("", Encoding.UTF8, "application/json"));
                Console.WriteLine("http request completed");
                // await _hub.Clients.All.SendAsync("transferExecuted", "done");
            }
            catch (System.Exception e)
            {
                Console.WriteLine(e);
            }
        }

        #region helper

        private static long ServerTime(string apiKey)
        {
            string apiUrl = string.Format("https://api.binance.com/api/v3/time");
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
using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using MarginCoin.Class;

namespace MarginCoin.Misc
{
    public class BinanceHelper
    {   
        private async void Buy(decimal amount)
        {
            string amount2 = amount.ToString().Replace(",", ".");
            try
            {
                string secretKey = Environment.GetEnvironmentVariable("BINANCE_SECRET_KEY");
                string apiKey = "lJ1rj5uEaCGEzd6RdXE5P6Em7oEc1Kp0bMXbcy7MoKFNEaajhEr873xzAkX5C2Px";

                string parameters = $"timestamp={ServerTime(apiKey)}&recvWindow=60000&type=MAIN_FUNDING&asset=ADA&amount={amount2}";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"https://api3.binance.com/sapi/v1/asset/transfer?{parameters}&signature={signature}";

                Console.WriteLine(apiUrl);
                HttpHelper.PostApiData<CryptoTransaction>(new Uri(apiUrl), apiKey, new StringContent("", Encoding.UTF8, "application/json"));
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
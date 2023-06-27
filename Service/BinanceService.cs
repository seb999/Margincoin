using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using MarginCoin.Class;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MarginCoin.Misc;
using Binance.Spot.Models;
using System.Linq;
using System.Web;
using System.Net;

namespace MarginCoin.Service
{
    public class BinanceService : IBinanceService
    {
        const string testPublicKey = "HsKWfKtktmw07gqsCyK1TJThULUnAivnFxF13vFUZf4WjLJXsbwmaPOIgw5rNAuQ";  //for https://testnet.binance.vision/
        const string testSecretKey = "ncSzN6J4Efh8Xb53e1uYkuHCw9VFAemUKjCEPdwY5WtdbMJOAEzEIuP5qMrjKewX";
        const string prodPublicKey = "gIDNZ9OsVIUbvFEuLgOhZ3XoQRnwrJ8krkp3TAR2dxQxwYErmKC6GOsMy50LYGWy";

        static string prodSecretKey = Environment.GetEnvironmentVariable("BSK");
        public static string secretKey = "";
        public static string publicKey = "";
        public static string host = "";

        public string Interval { get; set; }
        public string Limit { get; set; }

        ILogger _logger;

        public BinanceService(ILogger<BinanceService> logger)
        {
            _logger = logger;
        }

        public List<BinancePrice> GetSymbolPrice ()
        {
            string apiUrl = $"https://api.binance.com/api/v3/ticker/price";
            List<BinancePrice> symbolPrice = HttpHelper.GetApiData<List<BinancePrice>>(new Uri(apiUrl));
            symbolPrice =  symbolPrice.Where(p => p.symbol.Contains("USDT")).ToList();
            
            return symbolPrice;
        }

        public List<BinanceAsset> Asset(ref System.Net.HttpStatusCode httpStatusCode)
        {
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);

                string parameters = $"timestamp={ServerTime(publicKey)}&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/sapi/v3/asset/getUserAsset?{parameters}&signature={signature}";

                var result = HttpHelper.PostApiData<List<BinanceAsset>>(new Uri(apiUrl), publicKey, new StringContent("", Encoding.UTF8, "application/json"), ref httpStatusCode);
                if (result != null)
                {
                    _logger.LogWarning(httpStatusCode.ToString(), "Get " + MyEnum.BinanceApiCall.Asset);
                    return result;
                }
                else
                {
                    _logger.LogWarning(httpStatusCode.ToString(), "Get " + MyEnum.BinanceApiCall.Asset);
                    return null;
                }
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, "Get " + MyEnum.BinanceApiCall.Asset);
                return null;
            }
        }

        public BinanceAccount Account(ref System.Net.HttpStatusCode httpStatusCode)
        {
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);

                string parameters = $"timestamp={ServerTime(publicKey)}&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/account?{parameters}&signature={signature}";

                if (!Global.isProd)
                {
                    apiUrl = $"{host}/api/v3/account?{parameters}&signature={signature}";
                }

                var result = HttpHelper.GetApiData<BinanceAccount>(new Uri(apiUrl), publicKey, ref httpStatusCode);
                if (result != null)
                {
                    _logger.LogWarning($"Get {MyEnum.BinanceApiCall.Account} {httpStatusCode.ToString()}");
                    return result;
                }
                else
                {
                    _logger.LogWarning($"Get {MyEnum.BinanceApiCall.Account} {httpStatusCode.ToString()}");
                    return null;
                }
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, $"Get {MyEnum.BinanceApiCall.Account}");
                return null;
            }
        }

        public void GetCandles(string symbol, ref List<List<Candle>> candleMatrix)
        {
            //Get data from Binance API
            string apiUrl = $"https://api3.binance.com/api/v3/klines?symbol={symbol}&interval={Interval}&limit=100";
            List<List<double>> coinQuotation = HttpHelper.GetApiData<List<List<double>>>(new Uri(apiUrl));
            List<Candle> candleList = new List<Candle>();
            candleList = TradeHelper.CreateCandleList(coinQuotation, symbol);
            candleMatrix.Add(candleList);
        }

        public BinanceOrder OrderStatus(string symbol, double orderId)
        {
            try
            {
                System.Net.HttpStatusCode httpStatusCode = System.Net.HttpStatusCode.NoContent;

                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&orderId={orderId}&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

                if (!Global.isProd)
                {
                    apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";
                }

                var result = HttpHelper.GetApiData<BinanceOrder>(new Uri(apiUrl), publicKey, ref httpStatusCode);
                _logger.LogWarning($"Get {MyEnum.BinanceApiCall.OrderStatus} {httpStatusCode.ToString()}");
                return result;
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, "Get " + MyEnum.BinanceApiCall.OrderStatus);
                return null;
            }
        }

        public BinanceOrder BuyMarket(string symbol, double quoteQty, ref System.Net.HttpStatusCode httpStatusCode)
        {
            string stringQty = quoteQty.ToString().Replace(",", ".");
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&quoteOrderQty={stringQty}&side=BUY&type=MARKET&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

                if (!Global.isProd)
                {
                    apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";
                }

                var result = HttpHelper.PostApiData<BinanceOrder>(new Uri(apiUrl), publicKey, new StringContent("", Encoding.UTF8, "application/json"), ref httpStatusCode);
                _logger.LogWarning($"Call {MyEnum.BinanceApiCall.BuyMarket} {symbol} {httpStatusCode.ToString()}");
                return result;
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, $"Call {MyEnum.BinanceApiCall.BuyMarket}");
                return null;
            }
        }

        public BinanceOrder SellMarket(string symbol, double qty, ref System.Net.HttpStatusCode httpStatusCode)
        {
            string stringQuantity = qty.ToString().Replace(",", ".");
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&quantity={stringQuantity}&side=SELL&type=MARKET&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

                if (!Global.isProd)
                {
                    apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";
                }

                var result = HttpHelper.PostApiData<BinanceOrder>(new Uri(apiUrl), publicKey, new StringContent("", Encoding.UTF8, "application/json"), ref httpStatusCode);
                _logger.LogWarning($"Call {MyEnum.BinanceApiCall.SellMarket} {symbol} {httpStatusCode.ToString()}");
                return result;
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, $"Call {MyEnum.BinanceApiCall.SellMarket}");
                return null;
            }
        }

        public BinanceOrder BuyLimit(string symbol, double quantity, MyEnum.TimeInForce timeInForce)
        {
            string stringQuantity = quantity.ToString().Replace(",", ".");
            System.Net.HttpStatusCode httpStatusCode = System.Net.HttpStatusCode.NoContent;
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&quantity={stringQuantity}&timeInForce={timeInForce.ToString()}&side=BUY&type=LIMIT&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

                if (!Global.isProd)
                {
                    apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";
                }
               
                var result = HttpHelper.PostApiData<BinanceOrder>(new Uri(apiUrl), publicKey, new StringContent("", Encoding.UTF8, "application/json"), ref httpStatusCode);
                _logger.LogWarning(httpStatusCode.ToString(), "Call " + MyEnum.BinanceApiCall.BuyLimit);
                return result;
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, $"Call {MyEnum.BinanceApiCall.BuyLimit}");
                return null;
            }
        }

        public BinanceOrder SellLimit(string symbol, double quantity, MyEnum.TimeInForce timeInForce, ref System.Net.HttpStatusCode httpStatusCode)
        {
            string stringQuantity = quantity.ToString().Replace(",", ".");
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&quantity={stringQuantity}&timeInForce={timeInForce.ToString()}&side=SELL&type=LIMIT&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

                if (!Global.isProd)
                {
                    apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";
                }
               
                var result = HttpHelper.PostApiData<BinanceOrder>(new Uri(apiUrl), publicKey, new StringContent("", Encoding.UTF8, "application/json"), ref httpStatusCode);
                _logger.LogWarning($"Call {MyEnum.BinanceApiCall.SellLimit} {httpStatusCode.ToString()}");
             return result;
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, $"Call {MyEnum.BinanceApiCall.SellLimit}");
                return null;
            }
        }

        #region Get CoinMarketCap


        #endregion
        
        #region helper

        private static void SetEnv(ref string secretKey, ref string publicKey, ref string host)
        {
            if (Global.isProd)
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
            if (Global.isProd)
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
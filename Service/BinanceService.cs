using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using MarginCoin.Class;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MarginCoin.Misc;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using MarginCoin.Configuration;

namespace MarginCoin.Service
{
    public class BinanceService : IBinanceService
    {
        System.Net.HttpStatusCode httpStatusCode;
        private readonly IHubContext<SignalRHub> _hub;
        private readonly BinanceConfiguration _binanceConfig;
        private readonly ITradingState _tradingState;

        private string secretKey = "";
        private string publicKey = "";
        private string host = "";

        public string Interval { get; set; }
        public string Limit { get; set; }

        ILogger _logger;

        public BinanceService(ILogger<BinanceService> logger, IHubContext<SignalRHub> hub, IOptions<BinanceConfiguration> binanceConfig, ITradingState tradingState)
        {
            _logger = logger;
            _hub = hub;
            _binanceConfig = binanceConfig.Value;
            _tradingState = tradingState;

            // Override production secret key with environment variable if it exists
            var envSecretKey = Environment.GetEnvironmentVariable("BSK");
            if (!string.IsNullOrEmpty(envSecretKey))
            {
                _binanceConfig.Production.SecretKey = envSecretKey;
            }

            httpStatusCode = new System.Net.HttpStatusCode();
        }

        #region Get ticker / klines for symbol

        public List<BinancePrice> GetSymbolPrice()
        {
            string apiUrl = $"https://api.binance.com/api/v3/ticker/price";
            List<BinancePrice> symbolPrice = HttpHelper.GetApiData<List<BinancePrice>>(new Uri(apiUrl));
            symbolPrice = symbolPrice.Where(p => p.symbol.Contains("USDC")).ToList();
            return symbolPrice;
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

        public BinanceTicker Ticker(string symbol)
        {
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string apiUrl = $"{host}/api/v3/exchangeInfo?symbol={symbol}";
                var result = HttpHelper.GetApiData<BinanceTicker>(new Uri(apiUrl));
                _logger.LogInformation("Call {ApiCall} for {Symbol}: {StatusCode}", MyEnum.BinanceApiCall.Ticker, symbol, httpStatusCode);
                return result;
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, $"Call {MyEnum.BinanceApiCall.Ticker}");
                return null;
            }
        }

        #endregion

        #region Wallet
        public List<BinanceAsset> Asset()
        {
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);

                string parameters = $"timestamp={ServerTime(publicKey)}&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/sapi/v3/asset/getUserAsset?{parameters}&signature={signature}";

                var result = HttpHelper.PostApiData<List<BinanceAsset>>(apiUrl, publicKey, new StringContent("", Encoding.UTF8, "application/json"), ref httpStatusCode);
                _logger.LogInformation("Get {ApiCall}: {StatusCode}", MyEnum.BinanceApiCall.Asset, httpStatusCode);
                return result;
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, "Get " + MyEnum.BinanceApiCall.Asset);
                return null;
            }
        }

        public BinanceAccount Account()
        {
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/account?{parameters}&signature={signature}";

                //call Binance API
                var result = HttpHelper.GetApiData<BinanceAccount>(new Uri(apiUrl), publicKey, ref httpStatusCode);

                _logger.LogInformation("Get {ApiCall}: {StatusCode}", MyEnum.BinanceApiCall.Account, httpStatusCode);

                //log httpStatusCode result
                if (httpStatusCode != System.Net.HttpStatusCode.OK)
                {
                    _hub.Clients.All.SendAsync("httpRequestError", httpStatusCode.ToString());
                }

                return result;
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, $"Get {MyEnum.BinanceApiCall.Account}");
                return null;
            }
        }

        #endregion

        #region Order

        public BinanceOrder CancelSymbolOrder(string symbol)
        {
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/openOrders?{parameters}&signature={signature}";

                //call binance api
                var result = HttpHelper.DeleteApiData<BinanceOrder>(new Uri(apiUrl), publicKey, ref httpStatusCode);

                _logger.LogInformation("Call {ApiCall}: {StatusCode}", MyEnum.BinanceApiCall.CancelSymbolOrder, httpStatusCode);

                if (httpStatusCode != System.Net.HttpStatusCode.OK)
                {
                    _hub.Clients.All.SendAsync("httpRequestError", httpStatusCode.ToString());
                }
                return result;
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, "Call {ApiCall}", MyEnum.BinanceApiCall.CancelSymbolOrder);
                return null;
            }
        }

        public BinanceOrder CancelOrder(string symbol, double orderId)
        {
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&orderId={orderId}&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

                //call binance api
                var result = HttpHelper.DeleteApiData<BinanceOrder>(new Uri(apiUrl), publicKey, ref httpStatusCode);

                _logger.LogInformation("Call {ApiCall}: {StatusCode}", MyEnum.BinanceApiCall.CancelOrder, httpStatusCode);

                if (httpStatusCode != System.Net.HttpStatusCode.OK)
                {
                    _hub.Clients.All.SendAsync("httpRequestError", httpStatusCode.ToString());
                }
                return result;
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, "Call {ApiCall}", MyEnum.BinanceApiCall.CancelOrder);
                return null;
            }
        }

        public BinanceOrder OrderStatus(string symbol, double orderId)
        {
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&orderId={orderId}&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

                //call binance api
                var result = HttpHelper.GetApiData<BinanceOrder>(new Uri(apiUrl), publicKey, ref httpStatusCode);

                //log httpStatusCode result
                _logger.LogInformation("Get {ApiCall}: {StatusCode}", MyEnum.BinanceApiCall.OrderStatus, httpStatusCode);

                if (httpStatusCode != System.Net.HttpStatusCode.OK)
                {
                    _hub.Clients.All.SendAsync("httpRequestError", httpStatusCode.ToString());
                }
                return result;
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, "Get " + MyEnum.BinanceApiCall.OrderStatus);
                return null;
            }
        }

        public BinanceOrder BuyMarket(string symbol, double quoteQty)
        {
            string stringQty = quoteQty.ToString().Replace(",", ".");
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&quoteOrderQty={stringQty}&side=BUY&type=MARKET&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

                //Call Binance API
                var result = HttpHelper.PostApiData<BinanceOrder>(apiUrl, publicKey, new StringContent("", Encoding.UTF8, "application/json"), ref httpStatusCode);

                //log httpStatusCode result
                _logger.LogInformation("Call {ApiCall} for {Symbol}: {StatusCode}", MyEnum.BinanceApiCall.BuyMarket, symbol, httpStatusCode);
                if (httpStatusCode != System.Net.HttpStatusCode.OK)
                {
                    _hub.Clients.All.SendAsync("httpRequestError", httpStatusCode.ToString());
                }
                return result;
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, $"Call {MyEnum.BinanceApiCall.BuyMarket}");
                return null;
            }
        }

        public BinanceOrder SellMarket(string symbol, double qty)
        {
            string stringQuantity = qty.ToString().Replace(",", ".");
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&quantity={stringQuantity}&side=SELL&type=MARKET&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

                //Call Binance API
                var result = HttpHelper.PostApiData<BinanceOrder>(apiUrl, publicKey, new StringContent("", Encoding.UTF8, "application/json"), ref httpStatusCode);

                //log httpStatusCode result
                _logger.LogInformation("Call {ApiCall} for {Symbol}: {StatusCode}", MyEnum.BinanceApiCall.SellMarket, symbol, httpStatusCode);
                if (httpStatusCode != System.Net.HttpStatusCode.OK)
                {
                    _hub.Clients.All.SendAsync("httpRequestError", httpStatusCode.ToString());
                }
                return result;
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, $"Call {MyEnum.BinanceApiCall.SellMarket}");
                return null;
            }
        }

        public BinanceOrder BuyLimit(string symbol, double qty, double price, MyEnum.TimeInForce timeInForce)
        {
            string stringQty = qty.ToString().Replace(",", ".");
            string stringPrice = price.ToString().Replace(",", ".");
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&quantity={stringQty}&price={stringPrice}&timeInForce={timeInForce.ToString()}&side=BUY&type=LIMIT&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

                //call binance api
                var result = HttpHelper.PostApiData<BinanceOrder>(apiUrl, publicKey, new StringContent("", Encoding.UTF8, "application/json"), ref httpStatusCode);

                //log httpStatusCode result
                _logger.LogInformation("Call {ApiCall}: {StatusCode}", MyEnum.BinanceApiCall.BuyLimit, httpStatusCode);
                if (httpStatusCode != System.Net.HttpStatusCode.OK)
                {
                    _hub.Clients.All.SendAsync("httpRequestError", httpStatusCode.ToString());
                }
                return result;
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, $"Call {MyEnum.BinanceApiCall.BuyLimit}");
                return null;
            }
        }

        public BinanceOrder SellLimit(string symbol, double quantity, double price, MyEnum.TimeInForce timeInForce)
        {
            string stringQuantity = quantity.ToString().Replace(",", ".");
            string stringPrice = price.ToString().Replace(",", ".");
            try
            {
                SetEnv(ref secretKey, ref publicKey, ref host);
                string parameters = $"timestamp={ServerTime(publicKey)}&symbol={symbol}&quantity={stringQuantity}&price={stringPrice}&timeInForce={timeInForce.ToString()}&side=SELL&type=LIMIT&recvWindow=60000";
                string signature = GetSignature(parameters, secretKey);
                string apiUrl = $"{host}/api/v3/order?{parameters}&signature={signature}";

                //call binance api
                var result = HttpHelper.PostApiData<BinanceOrder>(apiUrl, publicKey, new StringContent("", Encoding.UTF8, "application/json"), ref httpStatusCode);

                //log httpStatusCode result
                _logger.LogInformation("Call {ApiCall}: {StatusCode}", MyEnum.BinanceApiCall.SellLimit, httpStatusCode);
                if (httpStatusCode != System.Net.HttpStatusCode.OK)
                {
                    _hub.Clients.All.SendAsync("httpRequestError", httpStatusCode.ToString());
                }
                return result;
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, $"Call {MyEnum.BinanceApiCall.SellLimit}");
                return null;
            }
        }

        #endregion

        #region Binance API setings

        private void SetEnv(ref string secretKey, ref string publicKey, ref string host)
        {
            if (_tradingState.IsProd)
            {
                secretKey = _binanceConfig.Production.SecretKey;
                publicKey = _binanceConfig.Production.PublicKey;
                host = "https://api3.binance.com";
            }
            else
            {
                secretKey = _binanceConfig.Test.SecretKey;
                publicKey = _binanceConfig.Test.PublicKey;
                host = "https://testnet.binance.vision";
            }
        }

        private long ServerTime(string apiKey)
        {
            string apiUrl;
            if (_tradingState.IsProd)
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

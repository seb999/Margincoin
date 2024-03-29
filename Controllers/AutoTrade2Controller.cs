using MarginCoin.Class;
using MarginCoin.Misc;
using MarginCoin.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Binance.Spot;
using System.Net.WebSockets;
using System.Threading;
using static MarginCoin.Class.Prediction;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AutoTrade2Controller : ControllerBase
    {
        private IHubContext<SignalRHub> _hub;
        private readonly ApplicationDbContext _appDbContext;
        private List<Candle> candleList = new List<Candle>();
        private List<Candle> candleListMACD = new List<Candle>();
        private List<MarketStream> buffer = new List<MarketStream>();

        int nbrUp = 0;
        int nbrDown = 0;

        bool buyOnHold = true;

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////------------SETTINGS----------/////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        string interval = "1h";   //1h seem to give better result
        int numberPreviousCandle = 2;

        //move stop lose to buy price when current price raise over:1.2%
        double secureNoLose = 1.016;

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////------------END SETTINGS----------/////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////

        //Default value for coin web socket
        MarketDataWebSocket ws = new MarketDataWebSocket("!ticker@arr");
        MarketDataWebSocket ws3 = new MarketDataWebSocket("wss://stream.binance.com:9443/ws/!ticker@arr");

        public AutoTrade2Controller(IHubContext<SignalRHub> hub, [FromServices] ApplicationDbContext appDbContext)
        {
            _hub = hub;
            _appDbContext = appDbContext;
        }

        [HttpGet("[action]")]
        public async Task<string> MonitorMarketAsync()
        {
            var onlyOneMessage = new TaskCompletionSource<string>();
            string dataResult = "";

            ws.OnMessageReceived(
                (data) =>
            {
                dataResult += data;
                if (dataResult.Contains("}]"))
                {
                    if (dataResult.Length > (dataResult.IndexOf("]") + 1))
                    {
                        dataResult = dataResult.Remove(dataResult.IndexOf("]") + 1);
                    }

                    List<MarketStream> marketStreamList = Helper.deserializeHelper<List<MarketStream>>(dataResult);

                    marketStreamList = marketStreamList.Where(p => p.s.Contains("USDT") && !p.s.Contains("DOWNUSDT")).Select(p => p).OrderByDescending(p => p.P).ToList();

                    TradeHelper.BufferMarketStream(marketStreamList, ref buffer);

                    nbrUp = buffer.Where(pred => pred.P >= 0).Count();
                    nbrDown = buffer.Where(pred => pred.P < 0).Count();

                    ProcessMarketStream(buffer);
                    _hub.Clients.All.SendAsync("trading");

                    dataResult = "";
                }
                return Task.CompletedTask;

            }, CancellationToken.None);

            await ws.ConnectAsync(CancellationToken.None);
            string message = await onlyOneMessage.Task;
            await ws.DisconnectAsync(CancellationToken.None);

            return "";
        }

        ///////////////////////////////////////////////////////////////////////////
        //////////////////////       ALGORYTHME       /////////////////////////////
        // ////////////////////////////////////////////////////////////////////////
        private void ProcessMarketStream(List<MarketStream> marketStreamList)
        {
            MarketStream marketFirstCoin = marketStreamList[0];
            MarketStream marketSecondCoin = marketStreamList[1];
            Order activeOrder = GetActiveOrder();
            TradeHelper.DisplayStatus(activeOrder, marketStreamList);

            if (!TradeHelper.DataQualityCheck(marketStreamList)) return;

            if (GetActiveOrder() != null)
            {
                CheckStopLose(marketStreamList);
            }
            else
            {
                //if (marketFirstCoin.P > marketSecondCoin.P * 1.02)
                if (marketFirstCoin.P > marketSecondCoin.P * 1.01)
                {
                    if (candleList.Count == 0)
                    {
                        GetCandles(marketFirstCoin.s);
                        OpenWebSocketOnSpot(marketFirstCoin.s);
                    }

                    if (candleList.Last().s != marketFirstCoin.s)
                    {
                        ws3.DisconnectAsync(CancellationToken.None);
                        GetCandles(marketFirstCoin.s);
                        OpenWebSocketOnSpot(marketFirstCoin.s);
                    }

                    if (CheckCandle(numberPreviousCandle, marketFirstCoin) && !buyOnHold)
                    {
                        Console.WriteLine($"New top coin {marketFirstCoin.s} : open trade RSI :{candleList.Last().Rsi}");
                        OpenTrade(marketFirstCoin);
                    }
                }
            }
        }

        private bool CheckCandle(int numberCandle, MarketStream marketFirstCoin)
        {   //Question : why using the marketFirstCoin parameter as we have the last value in the last candle in the list

            if (candleList.Count < 2) return false;

            if (marketFirstCoin.s != candleList.Last().s)
            {
                Console.WriteLine("Inconsistancy in candle list");
                return false;
            }

            //0 - Don't trade if only 14% coins are up over the last 24h
            if ((double)nbrUp < 40)
            {
                return false;
            }

            //1 - Previous candles are green
            for (int i = candleList.Count - numberCandle; i < candleList.Count; i++)
            {
                Console.WriteLine(TradeHelper.CandleColor(candleList[i]));
                if ((TradeHelper.CandleColor(candleList[i]) == "green") && candleList[i].c > candleList[i - 1].c)
                {
                    continue;
                }
                else
                {
                    return false;
                }
            }

            //2 - The current should be higther than the previous candle + 1/10
            if (marketFirstCoin.c < (candleList[candleList.Count - 2].c + (candleList[candleList.Count - 2].h - candleList[candleList.Count - 2].c) / 8))
            {
                return false;
            }

            //3 - If MACD 100 don't buy
            if (candleList.Last().Macd == 100)
            {
                return false;
            }

            //4 - RSI should be lower than 72 or RSI lower 80 if we already trade the coin
            if (candleList.Last().Rsi < 72 || (GetLastOrder().Symbol == marketFirstCoin.s && candleList.Last().Rsi < 80))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private void CheckStopLose(List<MarketStream> marketStreamList)
        {
            Order activeOrder = _appDbContext.Order.Where(p => p.Id == GetActiveOrder().Id).Select(p => p).FirstOrDefault();

            double lastPrice = marketStreamList.Where(p => p.s == activeOrder.Symbol).Select(p => p.c).FirstOrDefault();
            double highPrice = marketStreamList.Where(p => p.s == activeOrder.Symbol).Select(p => p.h).FirstOrDefault();

            if (lastPrice <= activeOrder.StopLose)
            {
                Console.WriteLine("Close trade : stop lose ");
                buyOnHold = true;
                CloseTrade(marketStreamList, $"Stop Lose");
            }
            else if (lastPrice <= (activeOrder.HighPrice * (1 - (activeOrder.TakeProfit / 100))))
            {
                Console.WriteLine("Close trade : 4% bellow Higher ");
                buyOnHold = true;
                CloseTrade(marketStreamList, "4% Limit");
            }

            SaveHighLow(marketStreamList[0], activeOrder);

            UpdateStopLose(lastPrice, activeOrder);

            UpdateTakeProfit(lastPrice, activeOrder);
        }


        #region Database accessor

        private void CloseTrade(List<MarketStream> marketStreamList, string closeType)
        {

            Order myOrder = _appDbContext.Order.Where(p => p.Id == GetActiveOrder().Id).Select(p => p).FirstOrDefault();
            double closePrice = marketStreamList.Where(p => p.s == myOrder.Symbol).Select(p => p.c).FirstOrDefault();
            if (closePrice == 0) return;

            myOrder.ClosePrice = closePrice;
            myOrder.Fee = myOrder.Fee + Math.Round((closePrice * myOrder.Quantity) / 100 * 0.1);
            myOrder.IsClosed = 1;
            myOrder.Type = closeType;
            myOrder.Profit = Math.Round((closePrice - myOrder.OpenPrice) * myOrder.Quantity) - myOrder.Fee;
            myOrder.CloseDate = DateTime.Now.ToString();
            _appDbContext.SaveChanges();
            Console.Beep();

            _hub.Clients.All.SendAsync("newOrder");
        }

        private void OpenTrade(MarketStream myMarketStream)
        {
            //Console.Beep();
            Console.WriteLine("Open trade");

            if (GetActiveOrder() != null) return;

            List<ModelOutput2> prediction = AIHelper.GetPrediction(candleListMACD);

            //1-read from db template
            OrderTemplate orderTemplate = GetOrderTemplate();

            double quantity = orderTemplate.Amount / myMarketStream.c;
            Order myOrder = new Order();
            myOrder.OpenDate = DateTime.Now.ToString();
            myOrder.OpenPrice = myMarketStream.c;
            myOrder.HighPrice = myMarketStream.c;
            myOrder.LowPrice = myMarketStream.c;
            myOrder.Volume = myMarketStream.v;
            myOrder.TakeProfit = orderTemplate.TakeProfit;
            myOrder.StopLose = myMarketStream.c * (1 - (orderTemplate.StopLose / 100));
            myOrder.Quantity = quantity;
            myOrder.IsClosed = 0;
            myOrder.Fee = Math.Round((myMarketStream.c * quantity) / 100) * 0.1;
            myOrder.Symbol = myMarketStream.s;

            myOrder.RSI = candleList.Last().Rsi;
            myOrder.EMA = candleList.Last().Ema;
            myOrder.MACD = candleListMACD.Last().Macd;
            myOrder.MACDSign = candleListMACD.Last().MacdSign;
            myOrder.MACDHist = candleListMACD.Last().MacdHist;
            myOrder.MACDHist_1 = candleListMACD[candleList.Count - 2].MacdHist;
            myOrder.MACDHist_2 = candleListMACD[candleList.Count - 3].MacdHist;
            myOrder.MACDHist_3 = candleListMACD[candleList.Count - 4].MacdHist;
            myOrder.PredictionLBFGS = prediction[0].Prediction == true ? 1 : 0;
            myOrder.PredictionLDSVM = prediction[1].Prediction == true ? 1 : 0;
            myOrder.PredictionSDA = prediction[2].Prediction == true ? 1 : 0;

            myOrder.Lock = 0;
            myOrder.MarketTrend = $"{nbrUp}|{nbrDown}";

            _appDbContext.Order.Add(myOrder);
            _appDbContext.SaveChanges();
            _hub.Clients.All.SendAsync("newOrder");
        }

        private void UpdateStopLose(double lastPrice, Order activeOrder)
        {
            if (lastPrice >= activeOrder.OpenPrice * secureNoLose)
            {
                activeOrder.StopLose = activeOrder.OpenPrice;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }
        }

        private void UpdateTakeProfit(double currentPrice, Order activeOrder)
        {
            OrderTemplate orderTemplate = GetOrderTemplate();
            double pourcent = ((currentPrice - activeOrder.OpenPrice) / activeOrder.OpenPrice) * 100;

            if (pourcent <= 0) return;

            if (2 < pourcent && pourcent <= 3)
            {
                activeOrder.TakeProfit = orderTemplate.TakeProfit - 0.4;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }

            if (3 < pourcent && pourcent <= 4)
            {
                activeOrder.TakeProfit = orderTemplate.TakeProfit - 0.5;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }
            if (4 < pourcent && pourcent <= 5)
            {
                activeOrder.TakeProfit = orderTemplate.TakeProfit - 0.6;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }
            if (5 < pourcent)
            {
                activeOrder.TakeProfit = orderTemplate.TakeProfit - 0.7;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }
        }

        #endregion

        #region Helper

        private async void OpenWebSocketOnSpot(string symbol)
        {
            if (symbol != candleList.Last().s)
            {
                await ws3.DisconnectAsync(CancellationToken.None);
            }

            ws3 = new MarketDataWebSocket($"{symbol.ToLower()}@kline_{interval}");
            var onlyOneMessage = new TaskCompletionSource<string>();

            ws3.OnMessageReceived(
                (data) =>
            {
                data = data.Remove(data.IndexOf("}}") + 2);
                var stream = Helper.deserializeHelper<StreamData>(data);

                if (!stream.k.x)
                {
                    if (candleList.Count > 0) candleList = candleList.SkipLast(1).ToList();
                    if (candleListMACD.Count > 0) candleListMACD = candleListMACD.SkipLast(1).ToList();
                }
                else
                {
                    Console.WriteLine("New candle save");
                    buyOnHold = false;
                }

                Candle newCandle = new Candle()
                {
                    s = stream.k.s,
                    o = stream.k.o,
                    h = stream.k.h,
                    l = stream.k.l,
                    c = stream.k.c,
                };
                candleList.Add(newCandle);
                candleListMACD.Add(newCandle);

                TradeIndicator.CalculateIndicator(ref candleList);
                TradeIndicator.CalculateIndicator(ref candleListMACD);
                return Task.CompletedTask;

            }, CancellationToken.None);

            try
            {
                await ws3.ConnectAsync(CancellationToken.None);
                string message = await onlyOneMessage.Task;
                await ws3.DisconnectAsync(CancellationToken.None);
            }
            catch
            {
                Console.WriteLine("impossible to open websocket on " + symbol);
            }
            finally
            {
                ws3 = new MarketDataWebSocket($"{symbol.ToLower()}@kline_{interval}");
            }
        }

        private void GetCandles(string symbol)
        {
            candleList.Clear();
            if (GetLastOrder().Symbol == symbol)
            {
                buyOnHold = true;
            }
            else
            {
                buyOnHold = false;
            }

            //Get data from Binance API
            string apiUrl = $"https://api3.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit=100";
            List<List<double>> coinQuotation = HttpHelper.GetApiData<List<List<double>>>(new Uri(apiUrl));
            candleList = CreateCandleList(coinQuotation, symbol);

            //Get data from Binance API for second candle list 15m interval to process MACD indicator and AI
            string apiUrl2 = $"https://api3.binance.com/api/v3/klines?symbol={symbol}&interval=15m&limit=100";
            List<List<double>> coinQuotation2 = HttpHelper.GetApiData<List<List<double>>>(new Uri(apiUrl));
            candleListMACD = CreateCandleList(coinQuotation2, symbol);
        }

        private List<Candle> CreateCandleList(List<List<double>> coinQuotation, string symbol)
        {
            List<Candle> candleList = new List<Candle>();
            foreach (var item in coinQuotation)
            {
                Candle newCandle = new Candle()
                {
                    s = symbol,
                    T = item[0],
                    o = item[1],
                    h = item[2],
                    l = item[3],
                    c = item[4],
                    v = item[5],
                    t = item[6],
                    id = Guid.NewGuid().ToString(),
                };
                candleList.Add(newCandle);
            }
            TradeIndicator.CalculateIndicator(ref candleList);
            return candleList;
        }

        private OrderTemplate GetOrderTemplate()
        {
            return _appDbContext.OrderTemplate.Where(p => p.IsInactive != 1).Select(p => p).FirstOrDefault();
        }

        private Order GetActiveOrder()
        {
            return _appDbContext.Order.Where(p => p.IsClosed == 0).FirstOrDefault();
        }

        private Order GetLastOrder()
        {
            Order lastOrder = _appDbContext.Order.OrderByDescending(p => p.Id).Select(p => p).FirstOrDefault();
            if (lastOrder != null)
            {
                return lastOrder;
            }
            else
            {
                return new Order();
            }
        }

        private void SaveHighLow(MarketStream marketStreamCoin, Order activeOrder)
        {
            if (marketStreamCoin.s != activeOrder.Symbol) return;

            if (marketStreamCoin.c > activeOrder.HighPrice)
            {
                activeOrder.HighPrice = marketStreamCoin.c;
            }

            if (marketStreamCoin.c < activeOrder.LowPrice)
            {
                activeOrder.LowPrice = marketStreamCoin.c;
            }

            _appDbContext.Order.Update(activeOrder);
            _appDbContext.SaveChanges();
        }

        #endregion
    }
}
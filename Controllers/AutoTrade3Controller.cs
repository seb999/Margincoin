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
    public class AutoTrade3Controller : ControllerBase
    {
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////------------Global varibles----------//////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        private IHubContext<SignalRHub> _hub;
        private readonly ApplicationDbContext _appDbContext;
        private List<List<Candle>> candleMatrice = new List<List<Candle>>();
        private List<Candle> candleListMACD = new List<Candle>();
        private List<MarketStream> marketStreamOnSpot = new List<MarketStream>();
        int nbrUp = 0;
        int nbrDown = 0;
        bool buyOnHold = true;

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////------------SETTINGS----------/////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        string interval = "30m";   //1h seem to give better result
        int numberPreviousCandle = 2;
        //move stop lose to buy price when current price raise over:1.2%
        double secureNoLose = 1.016;
        //max trade that can be open
        int maxOpenTrade = 10;
        List<string> mySymbolList = new List<string>();

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        ///////////////////////////////-----------Constructor----------/////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        public AutoTrade3Controller(IHubContext<SignalRHub> hub, [FromServices] ApplicationDbContext appDbContext)
        {
            _hub = hub;
            _appDbContext = appDbContext;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////////-----------ALGORYTME----------/////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        [HttpGet("[action]")]
        public async Task<string> MonitorMarket()
        {
            mySymbolList = GetSymbolList();

            //Get historic candles and open a webSocket for each symbol in my list
            foreach (var symbol in mySymbolList)
            {
                GetCandles(symbol);

                OpenWebSocketOnSymbol(symbol);
            }

            //Open web socket on spot to get 24h change stream
            await OpenWebSocketOnSpot();

            return "";
        }

        private void GetCandles(string symbol)
        {
            //Get data from Binance API
            string apiUrl = $"https://api3.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit=100";
            List<List<double>> coinQuotation = HttpHelper.GetApiData<List<List<double>>>(new Uri(apiUrl));
            List<Candle> candleList = new List<Candle>();
            candleList = CreateCandleList(coinQuotation, symbol);
            candleMatrice.Add(candleList);
        }

        private async void OpenWebSocketOnSymbol(string symbol)
        {
            MarketDataWebSocket ws = new MarketDataWebSocket($"{symbol.ToLower()}@kline_{interval}");
            var onlyOneMessage = new TaskCompletionSource<string>();

            ws.OnMessageReceived(
                (data) =>
            {
                data = data.Remove(data.IndexOf("}}") + 2);
                var stream = Helper.deserializeHelper<StreamData>(data);
                var symbolIndex = 0;

                //get corresponding line in our Matrice
                for (int i = 0; i < candleMatrice.Count; i++)
                {
                    if (candleMatrice[i][0].s == stream.k.s)
                    {
                        symbolIndex = i;
                        break;
                    }
                }

                if (!stream.k.x)
                {
                    if (candleMatrice[symbolIndex].Count > 0) candleMatrice[symbolIndex] = candleMatrice[symbolIndex].SkipLast(1).ToList();
                }
                else
                {
                    Console.WriteLine($"New candle save : {stream.k.s}");
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
                candleMatrice[symbolIndex].Add(newCandle);
                List<Candle> candleListWithIndicators = TradeIndicator.CalculateIndicator(candleMatrice[symbolIndex]);
                candleMatrice[symbolIndex] = candleListWithIndicators;

                return Task.CompletedTask;

            }, CancellationToken.None);

            try
            {
                await ws.ConnectAsync(CancellationToken.None);
                string message = await onlyOneMessage.Task;
                await ws.DisconnectAsync(CancellationToken.None);
            }
            catch
            {
                Console.WriteLine("impossible to open websocket on " + symbol);
            }
            finally
            {
                ws = new MarketDataWebSocket($"{symbol.ToLower()}@kline_{interval}");
            }
        }

        public async Task<string> OpenWebSocketOnSpot()
        {
            MarketDataWebSocket ws1 = new MarketDataWebSocket("!ticker@arr");
            var onlyOneMessage = new TaskCompletionSource<string>();
            string dataResult = "";

            ws1.OnMessageReceived(
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

                    AutotradeHelper.BufferMarketStream(marketStreamList, ref marketStreamOnSpot);

                    nbrUp = marketStreamOnSpot.Where(pred => pred.P >= 0).Count();
                    nbrDown = marketStreamOnSpot.Where(pred => pred.P < 0).Count();

                    ProcessMarketMatrice();
                    _hub.Clients.All.SendAsync("trading");

                    dataResult = "";
                }
                return Task.CompletedTask;

            }, CancellationToken.None);

            await ws1.ConnectAsync(CancellationToken.None);
            string message = await onlyOneMessage.Task;
            await ws1.DisconnectAsync(CancellationToken.None);

            return "";
        }

        private void ProcessMarketMatrice()
        {
            try
            {
                //0-Check each open order if closing is needed
                List<Order> activeOrderList = GetActiveOrder();
                if (activeOrderList != null)
                {
                    activeOrderList.Where(p => CheckStopLose(p)).ToList();
                }

                //1- Get list of symbol spot (24h variation)
                marketStreamOnSpot = marketStreamOnSpot.Where(p => mySymbolList.Any(p1 => p1 == p.s)).OrderByDescending(p => p.P).ToList();

                //2-check candle (indicators, etc, not on hold ) and invest
                for (int i = 0; i < maxOpenTrade; i++)
                {
                    var symbol = marketStreamOnSpot[i].s;
                    List<Candle> symbolCandle = candleMatrice.Where(p => p.First().s == symbol).FirstOrDefault();  //get the line that coorespond to the symbol
                    if (CheckCandle(numberPreviousCandle, marketStreamOnSpot[i], symbolCandle) && !buyOnHold)
                    {
                        Console.WriteLine($"Open trade on {symbol}");
                        OpenTrade(marketStreamOnSpot[i], symbolCandle);
                    }
                }
            }
            catch (System.Exception)
            {
                return;
            }
        }

        private bool CheckCandle(int numberCandle, MarketStream symbolSpot, List<Candle> symbolCandle)
        {   //Question : why using the marketFirstCoin parameter as we have the last value in the last candle in the list

            if (symbolCandle.Count < 2) return false;

            if (symbolSpot.s != symbolCandle.Last().s)
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
            for (int i = symbolCandle.Count - numberCandle; i < symbolCandle.Count; i++)
            {
                Console.WriteLine(AutotradeHelper.CandleColor(symbolCandle[i]));
                if ((AutotradeHelper.CandleColor(symbolCandle[i]) == "green") && symbolCandle[i].c > symbolCandle[i - 1].c)
                {
                    continue;
                }
                else
                {
                    return false;
                }
            }

            //2 - The current should be higther than the previous candle + 1/10
            if (symbolSpot.c < (symbolCandle[symbolCandle.Count - 2].c + (symbolCandle[symbolCandle.Count - 2].h - symbolCandle[symbolCandle.Count - 2].c) / 8))
            {
                return false;
            }

            //3 - If MACD 100 don't buy
            if (symbolCandle.Last().Macd == 100)
            {
                return false;
            }

            //4 - RSI should be lower than 72 or RSI lower 80 if we already trade the coin
            if (symbolCandle.Last().Rsi < 72 || (GetLastOrder().Symbol == symbolSpot.s && symbolCandle.Last().Rsi < 80))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool CheckStopLose(Order activeOrder)
        {
            double lastPrice = 0;
            double highPrice = 0;
            Candle lastCandle = new Candle();

            //iteration the matrice to find the line for the symbol of the active order
            foreach (var symbolCandle in candleMatrice)
            {
                if (symbolCandle[0].s == activeOrder.Symbol)
                {
                    lastPrice = symbolCandle.Select(p => p.c).LastOrDefault();
                    highPrice = symbolCandle.Select(p => p.h).LastOrDefault();
                    lastCandle = symbolCandle.Select(p => p).LastOrDefault();
                    break;
                }
            }

            if (lastPrice <= activeOrder.StopLose)
            {
                Console.WriteLine("Close trade : stop lose ");
                buyOnHold = true;
                CloseTrade(activeOrder.Id, lastCandle, $"Stop Lose");
            }
            else if (lastPrice <= (activeOrder.HighPrice * (1 - (activeOrder.TakeProfit / 100))))
            {
                Console.WriteLine("Close trade : 4% bellow Higher ");
                buyOnHold = true;
                CloseTrade(activeOrder.Id, lastCandle, "4% Limit");
            }

            SaveHighLow(lastCandle, activeOrder);

            UpdateStopLose(lastPrice, activeOrder);

            UpdateTakeProfit(lastPrice, activeOrder);

            return true;
        }


        #region Database Trade accessor

        private void CloseTrade(int orderId, Candle lastCandle, string closeType)
        {

            Order myOrder = _appDbContext.Order.Where(p => p.Id == orderId).Select(p => p).FirstOrDefault();
            double closePrice = lastCandle.c;
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

        private void OpenTrade(MarketStream symbolSpot, List<Candle> symbolCandle)
        {
            //Console.Beep();
            Console.WriteLine("Open trade");

            if (GetActiveOrder() != null) return;

            List<ModelOutput> prediction = AIHelper.GetPrediction(candleListMACD);

            //1-read from db template
            OrderTemplate orderTemplate = GetOrderTemplate();

            double quantity = orderTemplate.Amount / symbolSpot.c;
            Order myOrder = new Order();
            myOrder.OpenDate = DateTime.Now.ToString();
            myOrder.OpenPrice = symbolSpot.c;
            myOrder.HighPrice = symbolSpot.c;
            myOrder.LowPrice = symbolSpot.c;
            myOrder.Volume = symbolSpot.v;
            myOrder.TakeProfit = orderTemplate.TakeProfit;
            myOrder.StopLose = symbolSpot.c * (1 - (orderTemplate.StopLose / 100));
            myOrder.Quantity = quantity;
            myOrder.IsClosed = 0;
            myOrder.Fee = Math.Round((symbolSpot.c * quantity) / 100) * 0.1;
            myOrder.Symbol = symbolSpot.s;

            myOrder.RSI = symbolCandle.Last().Rsi;
            myOrder.EMA = symbolCandle.Last().Ema;
            myOrder.MACD = candleListMACD.Last().Macd;
            myOrder.MACDSign = candleListMACD.Last().MacdSign;
            myOrder.MACDHist = candleListMACD.Last().MacdHist;
            myOrder.MACDHist_1 = candleListMACD[symbolCandle.Count - 2].MacdHist;
            myOrder.MACDHist_2 = candleListMACD[symbolCandle.Count - 3].MacdHist;
            myOrder.MACDHist_3 = candleListMACD[symbolCandle.Count - 4].MacdHist;
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

        private void SaveHighLow(Candle lastCandle, Order activeOrder)
        {
            if (lastCandle.c > activeOrder.HighPrice)
            {
                activeOrder.HighPrice = lastCandle.c;
            }

            if (lastCandle.c < activeOrder.LowPrice)
            {
                activeOrder.LowPrice = lastCandle.c;
            }

            _appDbContext.Order.Update(activeOrder);
            _appDbContext.SaveChanges();
        }

        #endregion

        #region Helper

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

        private List<Order> GetActiveOrder()
        {
            return _appDbContext.Order.Where(p => p.IsClosed == 0).ToList();
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

        private List<string> GetSymbolList()
        {
            return _appDbContext.Symbol.Select(p => p.SymbolName).ToList();
        }
        #endregion
    }
}
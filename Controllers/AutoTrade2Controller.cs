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

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AutoTrade2Controller : ControllerBase
    {
        private IHubContext<SignalRHub> _hub;
        private readonly ApplicationDbContext _appDbContext;
        private List<Candle> candleList = new List<Candle>();
        private List<MarketStream> buffer = new List<MarketStream>();

        int nbrUp = 0;
        int nbrDown = 0;

        bool buyOnHold = true;

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////------------SETTINGS----------/////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        string interval = "15m";
        int numberPreviousCandle = 3;

        //move stop lose to buy price when current price raise over:1.2%
        double secureNoLose = 1.012;

        //used to adjust stopLose to last price - offset
        double offset = 0.8; //in %

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
                    if(dataResult.Length > dataResult.IndexOf("]"))
                    {
                        dataResult = dataResult.Remove(dataResult.IndexOf("]") + 1);
                    }
                    
                    List<MarketStream> marketStreamList = Helper.deserializeHelper<List<MarketStream>>(dataResult);

                    marketStreamList = marketStreamList.Where(p => p.s.Contains("USDT") && !p.s.Contains("DOWNUSDT")).Select(p => p).OrderByDescending(p => p.P).ToList();

                    AutotradeHelper.BufferMarketStream(marketStreamList, ref buffer);

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
            AutotradeHelper.DisplayStatus(activeOrder, marketStreamList);

            if (!AutotradeHelper.DataQualityCheck(marketStreamList)) return;

            if (GetActiveOrder() != null)
            {
                CheckStopLose(marketStreamList);
            }
            else
            {
                if (marketFirstCoin.P > marketSecondCoin.P * 1.02)
                {
                    if (candleList.Count == 0)
                    {
                        candleList = GetCandles(marketFirstCoin.s);
                        OpenWebSocketOnSpot(marketFirstCoin.s);
                    }

                    if (candleList.Last().s != marketFirstCoin.s)
                    {
                        ws3.DisconnectAsync(CancellationToken.None);
                        candleList = GetCandles(marketFirstCoin.s);
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

            for (int j = candleList.Count - 2; j >= candleList.Count - numberCandle; j--)
            {
                if (marketFirstCoin.s != candleList[j].s)
                {
                    Console.WriteLine("Inconsistancy in candle list");
                    return false;
                }

                //green candle and c should be higher than previous c + (h-c)/10
                if (marketFirstCoin.c < (candleList[j].c + (candleList[j].h - candleList[j].c) / 10) && AutotradeHelper.CandleColor(candleList[j]) == "green")
                {
                    return false;
                }

                //red candle
                if (marketFirstCoin.c < (candleList[j].o + (candleList[j].h - candleList[j].o) / 10) && AutotradeHelper.CandleColor(candleList[j]) == "red")
                {
                    return false;
                }
            }

            for (int i = candleList.Count - numberCandle; i < candleList.Count; i++)
            {
                Console.WriteLine(AutotradeHelper.CandleColor(candleList[i]));
                if ((AutotradeHelper.CandleColor(candleList[i]) == "green") && candleList[i].c > candleList[i - 1].c)
                {
                    continue;
                }
                else
                {
                    return false;
                }
            }

            if (candleList.Last().Rsi < 73 || (GetLastOrder().Symbol == marketFirstCoin.s && candleList.Last().Rsi < 93))
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
                if (activeOrder.Lock > 0)
                {
                    Console.WriteLine($"Close trade : stop lose Lock{activeOrder.Lock}");
                    buyOnHold = true;
                    CloseTrade(marketStreamList, $"Stop Lose Lock{activeOrder.Lock}");
                }
                else
                {
                    Console.WriteLine("Close trade : stop lose ");
                    buyOnHold = true;
                    CloseTrade(marketStreamList, $"Stop Lose");
                }
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

            //1-read from db template
            OrderTemplate orderTemplate = GetOrderTemplate();

            double quantity = orderTemplate.Amount / myMarketStream.c;
            Order myOrder = new Order();
            myOrder.OpenDate = DateTime.Now.ToString();
            myOrder.OpenPrice = myMarketStream.c;
            myOrder.HighPrice = myMarketStream.c;
            myOrder.LowPrice = myMarketStream.c;
            myOrder.TakeProfit = orderTemplate.TakeProfit;
            myOrder.StopLose = myMarketStream.c * (1 - (orderTemplate.StopLose / 100));
            myOrder.Quantity = quantity;
            myOrder.IsClosed = 0;
            myOrder.Fee = Math.Round((myMarketStream.c * quantity) / 100) * 0.1;
            myOrder.Symbol = myMarketStream.s;
            myOrder.RSI = candleList.Last().Rsi;
            myOrder.MACDHist = candleList.Last().MacdHist;
            myOrder.MACD = candleList.Last().Macd;
            myOrder.MACDSign = candleList.Last().MacdSign;
            myOrder.EMA = candleList.Last().Ema;
            myOrder.Lock = 0;
            myOrder.MarketTrend = $"{nbrUp}|{nbrDown}";

            _appDbContext.Order.Add(myOrder);
            _appDbContext.SaveChanges();
            _hub.Clients.All.SendAsync("newOrder");
        }

        private void UpdateStopLose(double lastPrice, Order activeOrder)
        {
            if (lastPrice >= activeOrder.OpenPrice * secureNoLose && activeOrder.Lock == 0)
            {
                activeOrder.StopLose = activeOrder.OpenPrice;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }

            // if ((lastPrice - activeOrder.OpenPrice) * activeOrder.Quantity > 200 && activeOrder.Lock == 0)
            // {
            //     activeOrder.StopLose = lastPrice * (1 - offset / 100);
            //     activeOrder.Lock = 1;
            //     _appDbContext.Order.Update(activeOrder);
            //     _appDbContext.SaveChanges();
            // }

            // if ((lastPrice - activeOrder.OpenPrice) * activeOrder.Quantity > 220 && activeOrder.Lock == 1)
            // {
            //     activeOrder.StopLose = lastPrice * (1 - offset / 100); ;
            //     activeOrder.Lock = 2;
            //     _appDbContext.Order.Update(activeOrder);
            //     _appDbContext.SaveChanges();
            // }

            // if ((lastPrice - activeOrder.OpenPrice) * activeOrder.Quantity > 270 && activeOrder.Lock == 2)
            // {
            //     activeOrder.StopLose = lastPrice * (1 - offset / 100); ;
            //     activeOrder.Lock = 3;
            //     _appDbContext.Order.Update(activeOrder);
            //     _appDbContext.SaveChanges();
            // }

            // if ((lastPrice - activeOrder.OpenPrice) * activeOrder.Quantity > 320 && activeOrder.Lock == 3)
            // {
            //     activeOrder.StopLose = lastPrice * (1 - offset / 100); ;
            //     activeOrder.Lock = 4;
            //     _appDbContext.Order.Update(activeOrder);
            //     _appDbContext.SaveChanges();
            // }

            // if ((lastPrice - activeOrder.OpenPrice) * activeOrder.Quantity > 370 && activeOrder.Lock == 4)
            // {
            //     activeOrder.StopLose = lastPrice * (1 - offset / 100); ;
            //     activeOrder.Lock = 5;
            //     _appDbContext.Order.Update(activeOrder);
            //     _appDbContext.SaveChanges();
            // }

            //if (activeOrder.StopLose == activeOrder.OpenPrice) return;
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
            if(symbol != candleList.Last().s) {
                await ws3.DisconnectAsync(CancellationToken.None);
            }
            
            ws3 = new MarketDataWebSocket($"{symbol.ToLower()}@kline_{interval}");
            var onlyOneMessage = new TaskCompletionSource<string>();
 
            ws3.OnMessageReceived(
                (data) =>
            {
                data = data.Remove(data.IndexOf("}}")+2);
                var stream = Helper.deserializeHelper<StreamData>(data);

                if (!stream.k.x)
                {
                    candleList = candleList.SkipLast(1).ToList();
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
                TradeIndicator.CalculateIndicator(ref candleList);
                return Task.CompletedTask;

            }, CancellationToken.None);

            await ws3.ConnectAsync(CancellationToken.None);
            string message = await onlyOneMessage.Task;
            await ws3.DisconnectAsync(CancellationToken.None);
        }

        private List<Candle> GetCandles(string symbol)
        {
            candleList.Clear();
            buyOnHold = false;
            string apiUrl = $"https://api3.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit=100";
            //Get data from Binance API
            List<List<double>> coinQuotation = HttpHelper.GetApiData<List<List<double>>>(new Uri(apiUrl));

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
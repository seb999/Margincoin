using MarginCoin.Class;
using MarginCoin.Misc;
using MarginCoin.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WebSocketSharp;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AutoTrade2Controller : ControllerBase
    {
        private IHubContext<SignalRHub> _hub;
        private readonly ApplicationDbContext _appDbContext;
        private List<Candle> candleList = new List<Candle>();

        bool buyOnHold = true;

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////------------SETTINGS----------/////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        string interval = "15m";
        int numberPreviousCandle = 4;

        //move stop lose to buy price when current price raise over:1.2%
        double secureNoLose = 1.012;

        //Default value for coin web socket
        WebSocket ws2 = new WebSocket($"wss://stream.binance.com:9443/ws/btcusdt@kline_5m");

        public AutoTrade2Controller(IHubContext<SignalRHub> hub, [FromServices] ApplicationDbContext appDbContext)
        {
            _hub = hub;
            _appDbContext = appDbContext;
        }

        [HttpGet("[action]")]
        public string MonitorMarket()
        {
            var ws1 = new WebSocket("wss://stream.binance.com:9443/ws/!ticker@arr");

            var pingTimer = new System.Timers.Timer(5000);
            pingTimer.Elapsed += (sender, args) =>
            {
                ws1.Ping();
            };
            pingTimer.Enabled = false;

            ws1.OnMessage += (sender, e) =>
            {
                List<MarketStream> marketStreamList = Helper.deserializeHelper<List<MarketStream>>(e.Data);
                marketStreamList = marketStreamList.Where(p => p.s.Contains("USDT") && !p.s.Contains("DOWNUSDT")).Select(p => p).OrderByDescending(p => p.P).ToList();
                ProcessMarketStream(marketStreamList);
                _hub.Clients.All.SendAsync("trading");
            };

            ws1.OnOpen += (sender, args) =>
            {
                pingTimer.Enabled = true;
            };

            ws1.OnClose += (sender, args) =>
           {
               pingTimer.Enabled = false;
               ws1.Connect();
               Console.WriteLine("Re-open websocket connection");
           };

            ws1.Connect();

            while (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                Task.Delay(2000);
            }
            ws1.Close();
            return "";
        }

        private void ProcessMarketStream(List<MarketStream> marketStreamList)
        {
            MarketStream marketFirstCoin = marketStreamList[0];
            MarketStream marketSecondCoin = marketStreamList[1];
            Spot dbSpot = GetSpot();
            Order activeOrder = GetActiveOrder();

            InitProcess(dbSpot, marketFirstCoin, activeOrder);
            DisplayStatus(activeOrder, marketStreamList);

            if (!DataQualityCheck(marketStreamList)) return;

            ///////////////////////////////////////////////////////////////////////////
            //////////////////////       ALGORYTHME       /////////////////////////////
            // ////////////////////////////////////////////////////////////////////////
            if (dbSpot.s != marketFirstCoin.s && marketFirstCoin.P > marketSecondCoin.P * 1.02)
            {
                candleList = GetCandles(marketFirstCoin.s);

                //First check : the previous candle should be green and this one too
                //Second check : current price should be higther than last 3 higher cndles and the candle is closed : go for it
                if (CheckCandle(numberPreviousCandle, marketFirstCoin))
                {
                    Console.WriteLine($"New top coin {marketFirstCoin.s} : open trade RSI :{candleList.Last().Rsi}");

                    if (GetActiveOrder() != null)
                    {
                        CloseTrade(marketStreamList, "New Coin");
                    }

                    OpenTrade(marketFirstCoin);
                    SaveSpot(marketFirstCoin);
                    ws2.Close();
                    OpenWebSocketOnSpot(marketFirstCoin.s);  //Just save candel in a list
                }
                else
                {
                    if (GetActiveOrder() != null)
                    {
                        CheckStopLose(marketStreamList);
                    }
                }
            }
            else
            {
                if (GetActiveOrder() != null)
                {
                    CheckStopLose(marketStreamList);
                }
                else
                {
                    if (dbSpot.s == marketFirstCoin.s) Rebuy(marketFirstCoin);
                }
            }
        }

        private void Rebuy(MarketStream marketFirstCoin)
        {
            //if previous candle of current one close higer than ATH, buy again
            if (ws2.IsAlive == false)
            {
                candleList = GetCandles(marketFirstCoin.s);
                OpenWebSocketOnSpot(marketFirstCoin.s);
            };

            //wait that previous candle where previous trade get terminated is closed!
            if (CheckCandle(numberPreviousCandle, marketFirstCoin) && !buyOnHold)
            {
                Console.WriteLine($"Rebuy : open trade RSI :{candleList.Last().Rsi}");
                OpenTrade(marketFirstCoin);
            }
        }

        private bool CheckCandle(int numberCandle, MarketStream marketFirstCoin)
        {   //Question : why using the marketFirstCoin parameter as we have the last value in the last candle in the list
            //If current candle and previous candle green we continue
            if (CandleColor(candleList.Last()) == "green" && CandleColor(candleList[candleList.Count - 2]) == "green")
            {

                for (int j = candleList.Count - 2; j >= candleList.Count - numberCandle; j--)
                {
                    if (marketFirstCoin.s != candleList[j].s)
                    {
                        Console.WriteLine("Inconsistancy in candle list");
                        return false;
                    }

                    //green candle and c should be higher than previous c + (h-c)/10
                    if (marketFirstCoin.c < (candleList[j].c + (candleList[j].h - candleList[j].c) / 10) && CandleColor(candleList[j]) == "green")
                    {
                        return false;
                    }

                    //red candle
                    if (marketFirstCoin.c < (candleList[j].o + (candleList[j].h - candleList[j].o) / 10) && CandleColor(candleList[j]) == "red")
                    {
                        return false;
                    }
                }

                return true;
            }
            else return false;
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
                CloseTrade(marketStreamList, "Stop Lose");
            }
            else if (lastPrice <= (activeOrder.HighPrice * (1 - (activeOrder.TakeProfit / 100))))
            {
                Console.WriteLine("Close trade : 4% bellow Higher ");
                buyOnHold = true;
                CloseTrade(marketStreamList, "4% Limit");
            }
            if((lastPrice - activeOrder.OpenPrice)*activeOrder.Quantity > 100)
            {
                Console.WriteLine("Close trade : made 100 ");
                buyOnHold = true;
                CloseTrade(marketStreamList, "100 balles");
            }


            UpdateStopLose(lastPrice, activeOrder);

            UpdateOrderHighLow(marketStreamList[0], activeOrder);

            UpdateTakeProfit(lastPrice, activeOrder);
        }


        #region Database accessor

        private void CloseTrade(List<MarketStream> marketStreamList, string closeType)
        {
            //Console.Beep();
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

            _hub.Clients.All.SendAsync("refreshUI");
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
            myOrder.MACD = candleList.Last().MacdHist;
            _appDbContext.Order.Add(myOrder);
            _appDbContext.SaveChanges();

            _hub.Clients.All.SendAsync("refreshUI");
        }

        private void UpdateStopLose(double lastPrice, Order activeOrder)
        {
            if (lastPrice >= activeOrder.OpenPrice * secureNoLose)
            {
                activeOrder.StopLose = activeOrder.OpenPrice;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }

            if (activeOrder.StopLose == activeOrder.OpenPrice) return;
        }

        private void UpdateOrderHighLow(MarketStream marketStreamCoin, Order activeOrder)
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

        private void UpdateTakeProfit(double currentPrice, Order activeOrder)
        {
            OrderTemplate orderTemplate = GetOrderTemplate();
            double pourcent = ((currentPrice - activeOrder.OpenPrice) / activeOrder.OpenPrice) * 100;

            if (pourcent <= 0) return;

            if (2 < pourcent && pourcent <= 3)
            {
                activeOrder.TakeProfit = orderTemplate.TakeProfit - 0.2;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }

            if (3 < pourcent && pourcent <= 4)
            {
                activeOrder.TakeProfit = orderTemplate.TakeProfit - 0.4;
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

        private void SaveSpot(MarketStream marketStreamCoin)
        {
            Spot mySpot = _appDbContext.Spot.FirstOrDefault();
            if (mySpot == null)
            {
                Spot newSpot = new Spot()
                {
                    s = marketStreamCoin.s,
                    P = marketStreamCoin.P,
                    OpenDate = DateTime.Now.ToString()
                };
                _appDbContext.Spot.Add(newSpot);
            }
            else
            {
                mySpot.s = marketStreamCoin.s;
                mySpot.P = marketStreamCoin.P;
                mySpot.OpenDate = DateTime.Now.ToString();
                _appDbContext.Spot.Update(mySpot);

            }
            _appDbContext.SaveChanges();
        }

        #endregion

        #region Helper

        private void OpenWebSocketOnSpot(string symbol)
        {
            ws2 = new WebSocket($"wss://stream.binance.com:9443/ws/{symbol.ToLower()}@kline_{interval}");
            var pingTimer = new System.Timers.Timer(5000);
            pingTimer.Elapsed += (sender, args) =>
            {
                ws2.Ping();
            };
            pingTimer.Enabled = false;

            ws2.OnMessage += (sender, e) =>
            {
                var stream = Helper.deserializeHelper<StreamData>(e.Data);

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
                    id = Guid.NewGuid().ToString(),
                };
                candleList.Add(newCandle);
                TradeIndicator.CalculateIndicator(ref candleList);
               

                // var stream = Helper.deserializeHelper<StreamData>(e.Data);
                // if (stream.k.x)
                // {
                //     Console.WriteLine("New candle save");
                //     Candle newCandle = new Candle()
                //     {
                //         s = stream.k.s,
                //         o = stream.k.o,
                //         h = stream.k.h,
                //         l = stream.k.l,
                //         c = stream.k.c,
                //         id = Guid.NewGuid().ToString(),
                //     };
                //     candleList.Add(newCandle);
                //     TradeIndicator.CalculateIndicator(ref candleList);

                //     buyOnHold = false;
                // }
            };

            ws2.OnOpen += (sender, args) =>
            {
                pingTimer.Enabled = true;
            };

            ws2.OnClose += (sender, args) =>
           {
               pingTimer.Enabled = false;
           };

            ws2.Connect();
        }

        private void DisplayStatus(Order activeOrder, List<MarketStream> marketStreamList)
        {
            if (activeOrder != null)
            {
                double currentPrice = marketStreamList.Where(p => p.s == activeOrder.Symbol).Select(p => p.c).FirstOrDefault();
                Console.WriteLine("Trading - Profit : " + Math.Round(((currentPrice - activeOrder.OpenPrice) * activeOrder.Quantity) - activeOrder.Fee));
            }
            else
            {
                Console.WriteLine("Trading - No active order");
            }
        }

        private bool DataQualityCheck(List<MarketStream> marketStreamList)
        {
            if (marketStreamList.Last().c != 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private void InitProcess(Spot dbSpot, MarketStream marketFirstCoin, Order activeOrder)
        {
            //First round : initialisation
            if (dbSpot == null)
            {
                SaveSpot(marketFirstCoin);
                return;
            }

            //Si on a pas d'ordre ouvert et le system en arret depuis plus de 15 minutes
            //alors on met a jour le spotCoin
            if (activeOrder == null)
            {
                if (dbSpot.s != marketFirstCoin.s && DateTime.Parse(dbSpot.OpenDate) < DateTime.Now.AddMinutes(-15))
                {
                    SaveSpot(marketFirstCoin);
                    buyOnHold = true;
                }
            }
        }

        private List<Candle> GetCandles(string symbol)
        {
            candleList.Clear();
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

        private Spot GetSpot()
        {
            return _appDbContext.Spot.OrderByDescending(p => p.P).FirstOrDefault();
        }
        private string CandleColor(Candle candle)
        {
            if (candle.c > candle.o) return "green";
            if (candle.c < candle.o) return "red";
            return "";
        }

        #endregion
    }
}
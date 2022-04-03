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


        int i = 0;

        public AutoTrade2Controller(IHubContext<SignalRHub> hub, [FromServices] ApplicationDbContext appDbContext)
        {
            _hub = hub;
            _appDbContext = appDbContext;
        }

        [HttpGet("[action]")]
        public async Task<string> MonitorMarket()
        {
            Console.Beep(); Console.Beep();
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
                marketStreamList = marketStreamList.Where(p => p.s.Contains("USDT")).Select(p => p).OrderByDescending(p => p.P).ToList();
                ProcessMarketStream(marketStreamList);
                _hub.Clients.All.SendAsync("trading", 0, 0, 0);
            };

            ws1.OnOpen += (sender, args) =>
            {
                pingTimer.Enabled = true;
            };

            ws1.OnClose += (sender, args) =>
           {
               pingTimer.Enabled = false;
           };

            ws1.Connect();

            while (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                await Task.Delay(2000);
            }
            ws1.Close();
            await _hub.Clients.All.SendAsync("tradingStopped");
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

            // ALGO :
            // IF (new spot) and (new spot 2% higher than previous spot) and (new spot candle 15min green) BUY!
            if (dbSpot.s != marketFirstCoin.s && marketFirstCoin.P > marketSecondCoin.P * 1.02)
            {
                Console.Beep();
                List<Candle> candleList = GetCandles(marketFirstCoin.s);

                //if it is a GREEN candle then we can buy  -ADD PREVIOUS CANDLE ALSO
                if (candleList.Last().c > candleList.Last().o)
                {
                    //Read /api/v3/klines (param : 15 min) et verifie que c >o
                    if (GetActiveOrder() != null)
                    {
                        CloseTrade(marketStreamList);
                    }

                    OpenTrade(marketFirstCoin); 
                    SaveSpot(marketFirstCoin);
                    OpenWebSocketOnSpot(marketFirstCoin);  //Just save candel in a list

                    _hub.Clients.All.SendAsync("refreshUI", 0, 0, 0);
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
                   //rebuy : use webSocketList to decide
                }
            }
        }

        #region trade methods

        private void OpenWebSocketOnSpot(MarketStream marketStream)
        {
            //if previous candle of current one close higer than ATH, buy again
             //BuyAgain();
                    var ws2 = new WebSocket($"wss://stream.binance.com:9443/ws/{marketStream.s.ToLower()}@kline_5m");

                    ws2.OnMessage += (sender, e) =>
                    {
                        Console.WriteLine("bonjour");
                       
                    };

                    ws2.OnOpen += (sender, args) =>
                    {
                        //pingTimer.Enabled = true;
                    };

                    ws2.OnClose += (sender, args) =>
                   {
                       //pingTimer.Enabled = false;
                   };

                    if (ws2.ReadyState == WebSocketState.Connecting)
                    {
                        ws2.Connect();
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
                CloseTrade(marketStreamList);
            }
            else if (lastPrice <= (activeOrder.HighPrice * (1 - (activeOrder.TakeProfit / 100))))
            {
                Console.WriteLine("Close trade : 5% bellow Higter ");
                CloseTrade(marketStreamList);
            }

            UpdateOrderHighLow(marketStreamList[0], activeOrder);

            _hub.Clients.All.SendAsync("refreshUI", 0, 0, 0);
        }
        private Order GetActiveOrder()
        {
            return _appDbContext.Order.Where(p => p.IsClosed == 0).FirstOrDefault();
        }

        private void OpenTrade(MarketStream myMarketStream)
        {
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
            myOrder.TakeProfit = 5;
            myOrder.StopLose = myMarketStream.c * (1 - (orderTemplate.StopLose / 100));
            myOrder.Quantity = quantity;
            myOrder.IsClosed = 0;
            myOrder.Margin = 1;
            myOrder.Fee = Math.Round((myMarketStream.c * quantity) / 100) * 0.1;
            myOrder.Symbol = myMarketStream.s;
            _appDbContext.Order.Add(myOrder);
            _appDbContext.SaveChanges();

            _hub.Clients.All.SendAsync("refreshUI", 0, 0, 0);
        }
        private void CloseTrade(List<MarketStream> marketStreamList)
        {
            Order myOrder = _appDbContext.Order.Where(p => p.Id == GetActiveOrder().Id).Select(p => p).FirstOrDefault();
            double closePrice = marketStreamList.Where(p => p.s == myOrder.Symbol).Select(p => p.c).FirstOrDefault();

            myOrder.ClosePrice = closePrice;
            myOrder.Fee = myOrder.Fee + Math.Round((closePrice * myOrder.Quantity) / 100 * 0.1);
            myOrder.IsClosed = 1;
            myOrder.Profit = Math.Round((closePrice - myOrder.OpenPrice) * myOrder.Quantity * myOrder.Margin) - myOrder.Fee;
            myOrder.CloseDate = DateTime.Now.ToString();
            _appDbContext.SaveChanges();

            _hub.Clients.All.SendAsync("refreshUI", 0, 0, 0);
        }



        #endregion

        #region Database accessor

        private void UpdateOrderHighLow(MarketStream marketStreamCoin, Order activeOrder)
        {

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

        private Spot GetSpot()
        {
            return _appDbContext.Spot.OrderByDescending(p => p.P).FirstOrDefault();
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

        private void SaveCandles()
        {

        }
        private void GetCandles()
        {

        }

        #endregion

        #region Helper

        private void DisplayStatus(Order activeOrder, List<MarketStream> marketStreamList)
        {
            i++;
            if (activeOrder != null)
            {
                double currentPrice = marketStreamList.Where(p => p.s == activeOrder.Symbol).Select(p => p.c).FirstOrDefault();
                Console.WriteLine("Trading " + i + " Profit : " + Math.Round(((currentPrice - activeOrder.OpenPrice) * activeOrder.Quantity * activeOrder.Margin) - activeOrder.Fee));
            }
            else
            {
                Console.WriteLine("Trading " + i + " ,No active order");
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
                }
            }
        }

        private List<Candle> GetCandles(string symbol)
        {
            string apiUrl = $"https://api3.binance.com/api/v3/klines?symbol={symbol}&interval=15m&limit=50";
            //Get data from Binance API
            List<List<double>> coinQuotation = HttpHelper.GetApiData<List<List<double>>>(new Uri(apiUrl));

            foreach (var item in coinQuotation)
            {
                Candle newCandle = new Candle()
                {
                    T = item[0],
                    o = item[1],
                    h = item[2],
                    l = item[3],
                    c = item[4],
                    v = item[5],
                    t = item[6],
                };
                candleList.Add(newCandle);
            }

            return candleList;
        }
        private OrderTemplate GetOrderTemplate()
        {
            return _appDbContext.OrderTemplate.Where(p => p.IsInactive != 1).Select(p => p).FirstOrDefault();
        }

        #endregion
    }
}
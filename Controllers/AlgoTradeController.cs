using Binance.Spot;
using MarginCoin.Class;
using MarginCoin.Misc;
using MarginCoin.Model;
using MarginCoin.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// https://stackoverflow.com/questions/22177491/how-to-force-entity-framework-to-always-get-updated-data-from-the-database

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AlgoTradeController : ControllerBase
    {

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////------------Global varibles----------//////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        #region Global variables

        private IHubContext<SignalRHub> _hub;
        private IBinanceService _binanceService;
        private IMLService _mlService;
        private IWatchDog _watchDog;
        private IWebSocket _webSocket;
        private IOrderService _orderService;

        ActionController actionController;
        private ApplicationDbContext _appDbContext;
        private ILogger _logger;
        private List<MarketStream> buffer = new List<MarketStream>();
        private List<MarketStream> marketStreamOnSpot = new List<MarketStream>();
        int nbrUp = 0;
        int nbrDown = 0;
        int i = 0;

        private readonly object candleMatrixLock = new object();

        #endregion  

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////------------SETTINGS----------/////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        #region Settings

        private readonly double orderOffset = 0.05;
        private readonly string spotTickerTime = "!ticker_4h@arr"; // !ticker@arr  or  !ticker_1h@arr 
        private readonly string interval = "30m";   //1h seem to give better result
        private readonly string maxCandle = "50";
        private readonly int prevCandleCount = 2;
        private readonly double stopLossPercentage = 2;
        private readonly double takeProfitPercentage = 0.5;
        private readonly int maxOpenTrade = 3;
        //Max amount to invest for each trade
        private readonly int quoteOrderQty = 1500;
        //Select short list of symbol or full list(on test server on 6 symbols allowed)
        private readonly int nbrOfSymbol = 18;   //Not below 10 as we trade later on the 10 best of this list

        #endregion

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        ///////////////////////////////-----------Constructor----------/////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        #region Constructor

        public AlgoTradeController(
            IHubContext<SignalRHub> hub,
            [FromServices] ApplicationDbContext appDbContext,
            ILogger<AlgoTradeController> logger,
            IBinanceService binanceService,
            IOrderService orderService,
            IMLService mLService,
            IWatchDog watchDog,
            IWebSocket webSocket)
        {
            _hub = hub;
            _binanceService = binanceService;
            _binanceService.Interval = interval;
            _binanceService.Limit = maxCandle;
            _appDbContext = appDbContext;
            _logger = logger;
            _orderService = orderService;
            _mlService = mLService;
            _watchDog = watchDog;
            _webSocket = webSocket;
            Global.syncBinanceSymbol = false;
            Global.nbrOfSymbol = nbrOfSymbol;
            Global.interval = interval;
            Global.stopLossPercentage = stopLossPercentage;
            Global.takeProfitPercentage = takeProfitPercentage;
            Global.orderOffset = orderOffset;
            Global.quoteOrderQty = quoteOrderQty;

            actionController = new ActionController(_appDbContext);
            if (Global.SymbolWeTrade.Count == 0)
            {
                Global.SymbolWeTrade = actionController.GetSymbolList();
                Global.SymbolBaseList = actionController.GetSymbolBaseList();
            }
        }

        #endregion

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////////-----------HTTP REQUEST-------/////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        #region http API request

        [HttpGet("[action]")]
        public async Task<string> MonitorMarket()
        {
            _logger.LogWarning("Start trading market...");
            _mlService.CleanImageFolder();

            if (!_watchDog.IsWebsocketSpotDown)
            {
                _mlService.InitML(UpdateSymbolWeTrade);
                _watchDog.InitWatchDog(RestartWebSocket);
            }
            else
            {
                await _webSocket.ws.DisconnectAsync(CancellationToken.None);
                await _webSocket.ws1.DisconnectAsync(CancellationToken.None);
                Global.candleMatrix.Clear();
                _logger.LogWarning($"Whatchdog kill all websock and restart it");
            }

            //open a webSocket for each symbol in my list
            foreach (var symbol in Global.SymbolWeTrade)
            {
                OpenWebSocketOnSymbol(symbol);
            }

            //add open order symbol not in SymbolWeTrade list
            foreach (var order in _orderService.GetActiveOrder())
            {
                if (Global.SymbolWeTrade.SingleOrDefault(p => p.SymbolName == order.Symbol) == null)
                {
                    var orderSymbol = Global.SymbolBaseList.SingleOrDefault(p => p.SymbolName == order.Symbol);
                    OpenWebSocketOnSymbol(orderSymbol);
                    Global.SymbolWeTrade.Add(orderSymbol);
                }
            }

            //Open websoket on Spot
            await OpenWebSocketOnSpot();

            _watchDog.Clear();
            return "";
        }

        [HttpGet("[action]/{id}/{lastPrice}")]
        public void CloseTrade(int id, double lastPrice)
        {
            Order myOrder = _appDbContext.Order.Where(p => p.Id == id).Select(p => p).FirstOrDefault();
            if (myOrder == null) return;

            var orderId = myOrder.Side == "BUY" ? myOrder.BuyOrderId : myOrder.SellOrderId;

            if (myOrder.Status == "NEW")
            {
                _binanceService.CancelOrder(myOrder.Symbol, orderId);
            }
            else
            {
                if (Global.isMarketOrder == true)
                    _orderService.SellMarket(myOrder, "by user");
                else
                    _orderService.SellLimit(myOrder, Global.marketStreamOnSpot.SingleOrDefault(p => p.s == myOrder.Symbol), "by user");
            }
        }

        [HttpGet("[action]")]
        public string UpdateML()
        {
            _mlService.UpdateML();
            return "";
        }

        [HttpGet("[action]")]
        public void SyncBinanceSymbol()
        {
            Global.syncBinanceSymbol = true;
        }

        [HttpGet("[action]")]
        public List<BinancePrice> GetSymbolPrice()
        {
            return _binanceService.GetSymbolPrice();
        }

        #endregion

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        //////////////////////////-----------WEB SOCKETS SUBSCRIPTION----------/////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        #region Web Sockets

        public void RestartWebSocket()
        {
            _logger.LogWarning($"Watchdog restart websockets");
            _watchDog.IsWebsocketSpotDown = true;
            _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.WebSocketStopped.ToString());
        }

        public void OpenWebSocketOnSymbol(Symbol symbol)
        {
            _binanceService.GetCandles(symbol.SymbolName, ref Global.candleMatrix);
            _webSocket.ws = new MarketDataWebSocket($"{symbol.SymbolName.ToLower()}@kline_{interval}");
            var onlyOneMessage = new TaskCompletionSource<string>();

            _webSocket.ws.OnMessageReceived(
                (data) =>
                {
                    data = data.Remove(data.IndexOf("}}") + 2);
                    var stream = Helper.deserializeHelper<StreamData>(data);
                    var symbolIndex = 0;

                    //get corresponding line in our Matrice
                    for (int i = 0; i < Global.candleMatrix.Count; i++)
                    {
                        if (Global.candleMatrix[i][0].s == stream.k.s)
                        {
                            symbolIndex = i;
                            UpdateMatrix(stream, Global.candleMatrix[i]);
                            break;
                        }
                    }

                    return Task.CompletedTask;

                }, CancellationToken.None);

            try
            {
                _webSocket.ws.ConnectAsync(CancellationToken.None);
                var message = onlyOneMessage.Task;
                _webSocket.ws.DisconnectAsync(CancellationToken.None);
            }
            catch
            {
                Console.WriteLine("impossible to open websocket on " + symbol);
            }
            finally
            {
                _webSocket.ws = new MarketDataWebSocket($"{symbol.SymbolName.ToLower()}@kline_{interval}");
            }
        }

        private void UpdateMatrix(StreamData stream, List<Candle> candleList)
        {
            Candle newCandle = new Candle()
            {
                s = stream.k.s,
                o = stream.k.o,
                h = stream.k.h,
                l = stream.k.l,
                c = stream.k.c,
                P = TradeHelper.CalculPourcentChange(stream.k.c, candleList, interval, 2),
            };

            if (!stream.k.x)
            {
                if (candleList.Count == 0) return;
                candleList = candleList.SkipLast(1).ToList();
                candleList.Add(newCandle);
            }
            else
            {
                candleList.Add(newCandle);
                Console.WriteLine($"New candle save : {stream.k.s}");
                _logger.LogWarning($"New candle save : {stream.k.s}");
                Global.onHold.Remove(stream.k.s);
            }

            //Calculate RSI / MACD / EMA
            candleList = TradeIndicator.CalculateIndicator(candleList);

            //Calculate the Average True Range ATR
            //Globals.candleMatrix[symbolIndex].ToList().Last().ATR = TradeIndicator.CalculateATR(Globals.candleMatrix[symbolIndex].ToList());

            //Calculate the slope of the MACD historic (derivative) 
            candleList.ToList().Last().MacdSlope = TradeHelper.CalculateMacdSlope(candleList.ToList(), interval).Slope;

            //We replace old list<candle> for the symbol with this new one
            for (int i = 0; i < Global.candleMatrix.Count; i++)
            {
                if (Global.candleMatrix[i][0].s == stream.k.s)
                {
                    Global.candleMatrix[i] = candleList;
                }
            }
            //We order the matrix list
            //Global.candleMatrix = Global.candleMatrix.OrderByDescending(p => p.Last().MacdSlope).ToList();
            Global.candleMatrix = Global.candleMatrix.OrderByDescending(p => p.Last().P).ToList();
        }

        public async Task OpenWebSocketOnSpot()
        {
            _webSocket.ws1 = new MarketDataWebSocket(spotTickerTime);
            var onlyOneMessage = new TaskCompletionSource<string>();
            string dataResult = "";

            _webSocket.ws1.OnMessageReceived(
                 async (data) =>
                 {
                     dataResult += data;
                     if (dataResult.Contains("}]"))
                     {
                         if (dataResult.Length > (dataResult.IndexOf("]") + 1))
                         {
                             dataResult = dataResult.Remove(dataResult.IndexOf("]") + 1);
                         }

                         List<MarketStream> marketStreamList = Helper.deserializeHelper<List<MarketStream>>(dataResult);
                         dataResult = "";  //we clean it immediatly to avoid a bug on new data coming

                         marketStreamList = marketStreamList.Where(p => p.s.Contains("USDT")).ToList();

                         TradeHelper.BufferMarketStream(marketStreamList, ref buffer);

                         //Update db symbol table with new coins from Binance
                         if (Global.syncBinanceSymbol)
                         {
                             Global.syncBinanceSymbol = false;
                             ActionController actionController = new ActionController(_appDbContext);
                             //Update list of symbol from binance
                             actionController.UpdateDbSymbol(buffer.Select(p => p.s).ToList());
                             //Update capitalisation and ranking from CoinMarketCap
                             actionController.UpdateCoinMarketCap();
                         }

                         nbrUp = buffer.Count(pred => pred.P >= 0);
                         nbrDown = buffer.Count(pred => pred.P < 0);

                         marketStreamOnSpot = buffer.Where(p => Global.SymbolBaseList.Any(p1 => p1.SymbolName == p.s)).OrderByDescending(p => p.P).ToList();

                         Global.marketStreamOnSpot = marketStreamOnSpot;

                         _watchDog.Clear();

                         if (Global.isTradingOpen)
                         {
                             await ProcessMarketMatrix();
                         }
                     }
                     //return Task.CompletedTask;

                 }, CancellationToken.None);

            await _webSocket.ws1.ConnectAsync(CancellationToken.None);
            string message = await onlyOneMessage.Task;
            await _webSocket.ws1.DisconnectAsync(CancellationToken.None);

            return;
        }

        public void UpdateSymbolWeTrade()
        {
            foreach (var symbol in marketStreamOnSpot.Take(3))
            {
                if (Global.SymbolWeTrade.Where(p => p.SymbolName == symbol.s).FirstOrDefault() == null)
                {
                    Symbol hotSymbol = Global.SymbolBaseList.Where(p => p.SymbolName == symbol.s).FirstOrDefault();
                    OpenWebSocketOnSymbol(hotSymbol);
                    Global.SymbolWeTrade.Add(hotSymbol);
                }
            }
        }

        #endregion

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////////-----------ALGORYTME----------/////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        #region Algo
        private async Task ProcessMarketMatrix()
        {
            try
            {
                ReviewPendingOrder();

                //Review open orders
                foreach (var item in _orderService.GetActiveOrder())
                {
                    ReviewOpenTrade(marketStreamOnSpot, item.Symbol);
                }

                if (_orderService.GetActiveOrder().Count < maxOpenTrade)
                {
                    foreach (var symbolCandelList in Global.candleMatrix.Take(10).ToList())
                    {
                        ReviewSpotMarket(marketStreamOnSpot, symbolCandelList);
                    }
                }

                await _hub.Clients.All.SendAsync("trading", JsonSerializer.Serialize(marketStreamOnSpot));
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ProcessMarketMatrix failled");
                return;
            }
            finally
            {
                _watchDog.Clear();
            }
        }

        private void ReviewSpotMarket(List<MarketStream> marketStreamList, List<Candle> symbolCandelList)
        {
            var symbolSpot = marketStreamOnSpot.Where(p => p.s == symbolCandelList.Last().s).FirstOrDefault();

            if (symbolSpot == null)
            {
                return;
            }

            var activeOrder = _orderService.GetActiveOrder().FirstOrDefault(p => p.Symbol == symbolSpot.s);
            var symbolCandle = Global.candleMatrix.Where(p => p.Last().s == symbolSpot.s).ToList().FirstOrDefault();
            var activeOrderCount = _orderService.GetActiveOrder().Count();

            if (activeOrder == null && activeOrderCount < maxOpenTrade)
            {
                if (EnterLongPosition(symbolSpot, symbolCandle))
                {
                    if (!Global.onHold.ContainsKey(symbolSpot.s))
                    {
                        Global.onHold.Add(symbolSpot.s, true);
                    }

                    Console.WriteLine($"Opening trade on {symbolSpot.s}");

                    if (Global.isMarketOrder == true)
                    {
                        _orderService.BuyMarket(symbolSpot, symbolCandle);
                    }
                    else
                    {
                        _orderService.BuyLimit(symbolSpot, symbolCandle);
                    }
                }


                if (Global.testBuyLimit == true)
                {
                    Global.testBuyLimit = false;
                    Console.WriteLine($"Opening trade on {symbolSpot.s}");
                    _orderService.BuyLimit(symbolSpot, symbolCandle);
                }

                if (symbolSpot.P < 0 && IsShort(symbolSpot, symbolCandle))
                {

                }
            }
        }

        private bool EnterLongPosition(MarketStream symbolSpot, List<Candle> symbolCandles)
        {
            const int MIN_CONSECUTIVE_UP_SYMBOL = 30;    //Used to stop trading if too many symbol down
            const int MAX_SPREAD = -5;                   //Used to ignore previous condition if market in freefall
            const double MIN_SCORE = 0.60;               //min AI score to invest
            const double MIN_POURCENT_UP = 0.2;            //User to identify strong movememnt and ignore micro up mouvement
            const double MIN_RSI = 40;
            const double MAX_RSI = 80;

            if (symbolSpot.P < 0)
            {
                return false;
            }

            if (nbrUp < MIN_CONSECUTIVE_UP_SYMBOL && symbolSpot.P > MAX_SPREAD)
            {
                return false;
            }

            // Check if there are enough candles to perform the analysis
            if (symbolCandles.Count > 2)
            {
                //Check if previous candles are green
                for (int i = symbolCandles.Count - prevCandleCount; i < symbolCandles.Count; i++)
                {
                    if (TradeHelper.CandleColor(symbolCandles[i]) != "green" || symbolCandles[i].c <= symbolCandles[i - 1].c)
                    {
                        return false;
                    }
                }

                //check that it is a strong movement by calculating the % increase on last candles
                //if I calculate P I get a thread safe error
                // if (TradeHelper.CalculPourcentChange(symbolCandles, prevCandleCount) < MIN_POURCENT_UP)
                // {
                //     isLong = false;
                // }
            }

            //Check AI prediction
            var mlPrediction = _mlService.MLPredList.ToList().FirstOrDefault(p => p.Symbol == symbolSpot.s);
            if (mlPrediction == null || mlPrediction.PredictedLabel != "up" || mlPrediction.Score[1] < MIN_SCORE)
            {
                return false;
            };

            //Check the symbol is not on hold
            if (Global.onHold.ContainsKey(symbolSpot.s) && Global.onHold[symbolSpot.s])
            {
                return false;
            };

            //Check the RSI
            if (symbolCandles.Last().Rsi < MIN_RSI || symbolCandles.Last().Rsi > MAX_RSI)
            {
                return false;
            };

            return true;
        }

        private void ReviewOpenTrade(List<MarketStream> marketStreamList, string symbol)
        {
            lock (candleMatrixLock)
            {
                var activeOrder = _orderService.GetActiveOrder().FirstOrDefault(p => p.Symbol == symbol);
                var symbolCandle = Global.candleMatrix.FirstOrDefault(p => p.Last().s == symbol)?.Last();
                var lastPrice = symbolCandle?.c;
                var highPrice = symbolCandle?.h;

                if (activeOrder == null || symbolCandle == null)
                    return;

                // Extracted method for logging and selling
                void LogAndSell(string message)
                {
                    Console.WriteLine($"Close trade: {message}");
                    if (Global.isMarketOrder)
                    {
                        _orderService.SellMarket(activeOrder, message);
                    }
                    else
                    {
                        _orderService.SellLimit(activeOrder, Global.marketStreamOnSpot.FirstOrDefault(p => p.s == symbol), message);
                    }
                }

                // Check time and close if necessary
                TimeSpan span = DateTime.Now.Subtract(DateTime.Parse(activeOrder.OpenDate));
                if (activeOrder.HighPrice <= activeOrder.OpenPrice && span.TotalMinutes > 15)
                {
                    LogAndSell("killed");
                    return ;
                }

                // Check stop loss
                if (lastPrice < activeOrder.StopLose)
                {
                    LogAndSell("stop lose");
                    return;
                }

                // Take profit
                if (lastPrice <= activeOrder.TakeProfit && lastPrice > activeOrder.OpenPrice)
                {
                    Console.WriteLine($"Close trade: take profit (price: {lastPrice} < take profit {activeOrder.TakeProfit})");
                    LogAndSell("Take profit");
                    return;
                }

                // AI close
                var predictedLabel = _mlService.MLPredList.FirstOrDefault(p => p.Symbol == activeOrder.Symbol)?.PredictedLabel;
                var score = _mlService.MLPredList.FirstOrDefault(p => p.Symbol == activeOrder.Symbol)?.Score[0];

                if (predictedLabel == "down" && score >= 0.97)
                {
                    LogAndSell("AI take profit");
                    return;
                }

                if (Global.isDbBusy)
                    return;

                // Reload only the required properties
                _appDbContext.Entry(activeOrder).Reload();
                _orderService.UpdateTakeProfit(Global.candleMatrix.FirstOrDefault(p => p.Last().s == symbol), activeOrder, takeProfitPercentage);
                _orderService.UpdateStopLoss(Global.candleMatrix.FirstOrDefault(p => p.Last().s == symbol), activeOrder);
                _orderService.SaveHighLow(Global.candleMatrix.FirstOrDefault(p => p.Last().s == symbol), activeOrder);
            }
        }

        private bool IsShort(MarketStream symbolSpot, List<Candle> symbolCandle)
        {
            return true;
        }

        private void ReviewPendingOrder()
        {
            if (i < 5)
            {
                i++;
                return;
            }
            else
            {
                i = 0;
                CheckSellOrder();
                CheckBuyOrder();
                return;
            }
        }

        private void CheckSellOrder()
        {
            //1 read on local db pending order buy or sell
            var dbOrderList = _appDbContext.Order.Where(p => p.Status != "FILLED" && p.Side == "SELL" && p.SellOrderId != 0).Select(p => p).ToList();

            //No order in the pool
            if (!dbOrderList.Any()) return;

            //For each pending order in local db
            foreach (var dbOrder in dbOrderList)
            {
                _appDbContext.Entry(dbOrder).Reload();   //we reload the entity from the database to get last changes !!!!

                var myBinanceOrder = _binanceService.OrderStatus(dbOrder.Symbol, dbOrder.SellOrderId);
                var storedDate = DateTime.ParseExact(dbOrder.OrderDate, "dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                var currentDateMinusOffset = DateTime.Now.AddSeconds(-50);
                var orderStatus = myBinanceOrder.status;

                if ((orderStatus == "NEW"
                || orderStatus == "PARTIALLY_FILLED")
                && storedDate.CompareTo(currentDateMinusOffset) <= 0)
                {
                    _binanceService.CancelOrder(myBinanceOrder.symbol, myBinanceOrder.orderId);
                }

                if (orderStatus == "PARTIALLY_FILLED")
                {
                    _orderService.UpdateSellOrderDb(dbOrder, myBinanceOrder, "");
                }

                if (orderStatus == "FILLED")
                {
                    _orderService.CloseOrderDb(dbOrder, myBinanceOrder);
                }

                if (orderStatus == "CANCELED"
                || orderStatus == "REJECTED"
                || orderStatus == "EXPIRED")
                {
                    _orderService.UpdateSellOrderDb(dbOrder, myBinanceOrder, "");
                    _orderService.RecycleOrderDb(dbOrder.Id);
                    Global.onHold.Remove(dbOrder.Symbol);
                }
            }

            _hub.Clients.All.SendAsync("refreshUI");
            return;
        }

        private async Task CheckBuyOrder()
        {
            //1 read on local db pending order buy or sell
            var dbOrderList = _appDbContext.Order.Where(p => p.Status != "FILLED" && p.Side == "BUY").Select(p => p).ToList();

            //No order in the pool
            if (!dbOrderList.Any())
                return;

            //For each pending order in local db
            foreach (var dbOrder in dbOrderList)
            {
                //we get the order status from binance pool
                var myBinanceOrder = _binanceService.OrderStatus(dbOrder.Symbol, dbOrder.BuyOrderId);
                var storedDate = DateTime.ParseExact(dbOrder.OpenDate, "dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                var currentDateMinusOffset = DateTime.Now.AddSeconds(-50);
                var orderStatus = myBinanceOrder.status;

                if ((orderStatus == "NEW" || orderStatus == "PARTIALLY_FILLED") && storedDate.CompareTo(currentDateMinusOffset) <= 0)
                {
                    _binanceService.CancelOrder(myBinanceOrder.symbol, myBinanceOrder.orderId);
                }

                if (orderStatus == "FILLED" || orderStatus == "PARTIALLY_FILLED")
                {
                    _orderService.UpdateBuyOrderDb(dbOrder, myBinanceOrder);
                }

                if (orderStatus == "CANCELED" || orderStatus == "REJECTED" || orderStatus == "EXPIRED")
                {
                    decimal.TryParse(myBinanceOrder.executedQty, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal executedQty);
                    if (executedQty == 0)
                    {
                        Global.onHold.Remove(dbOrder.Symbol);
                        _orderService.DeleteOrderDb(dbOrder.Id);
                    }
                    else
                    {
                        _orderService.UpdateBuyOrderDb(dbOrder, myBinanceOrder);
                        _orderService.RecycleOrderDb(dbOrder.Id);
                    }
                }
            }

            await _hub.Clients.All.SendAsync("refreshUI");
            return;
        }

        #endregion

        #region Debug

        [HttpGet("[action]")]
        public void TestBinanceBuy()
        {
            Global.testBuyLimit = true;
        }
        #endregion
    }
}
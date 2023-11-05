using Binance.Spot;
using MarginCoin.Class;
using MarginCoin.Misc;
using MarginCoin.Model;
using MarginCoin.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

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
        private IRepositoryService _repositoryService;
        private readonly ApplicationDbContext _appDbContext;
        private ILogger _logger;

        private List<MarketStream> buffer = new List<MarketStream>();
        private List<MarketStream> marketStreamOnSpot = new List<MarketStream>();
        private List<string> mySymbolList = new List<string>();
        int nbrUp = 0;
        int nbrDown = 0;

        private readonly object candleMatrixLock = new object();

        #endregion  

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////------------SETTINGS----------/////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        #region Settings    

        private readonly string interval = "30m";   //1h seem to give better result
        private readonly string maxCandle = "50";
        private readonly int prevCandleCount = 2;
        private readonly double stopLossPercentage = 2;
        private readonly double takeProfitPercentage = 0.5;
        private readonly int maxOpenTrade = 2;
        //Max amount to invest for each trade
        private readonly int quoteOrderQty = 3000;
        //Select short list of symbol or full list(on test server on 6 symbols allowed)
        private readonly bool fullSymbolList = true;
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
            IRepositoryService repositoryService,
            IMLService mLService,
            IWatchDog watchDog,
            IWebSocket webSocket)
        {
            _hub = hub;
            _binanceService = binanceService;
            _appDbContext = appDbContext;
            _logger = logger;
            _repositoryService = repositoryService;
            _mlService = mLService;
            _watchDog = watchDog;
            _webSocket = webSocket;

            Global.fullSymbolList = fullSymbolList;
            Global.syncBinanceSymbol = false;
            Global.fullSymbolList = fullSymbolList;
            Global.nbrOfSymbol = nbrOfSymbol;
            Global.interval = interval;


            //Get the list of symbol to trade from DB
            ActionController actionController = new ActionController(_appDbContext);
            mySymbolList = actionController.GetSymbolList();
            Global.AItradeSymbol = mySymbolList;

            _binanceService.Interval = interval;
            _binanceService.Limit = maxCandle;
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
                _mlService.InitML();
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
            foreach (var symbol in mySymbolList)
            {
                _binanceService.GetCandles(symbol, ref Global.candleMatrix);
                OpenWebSocketOnSymbol(symbol);
            }

            //Open websoket on Spot
            await OpenWebSocketOnSpot();

            _watchDog.Clear();
            return "";
        }

        [HttpGet("[action]/{orderId}/{lastPrice}")]
        public string CloseTrade(int orderId, double lastPrice)
        {
            Order myOrder = _appDbContext.Order.Where(p => p.Id == orderId).Select(p => p).FirstOrDefault();
            if (myOrder != null)
            {
                if (Global.onAir)
                {
                    Console.WriteLine("Close trade : by user");
                    Sell(orderId, "by user");
                }
                else
                {
                    Console.WriteLine("Close fake trade : by user");
                    SellFack(orderId, "by user", lastPrice);
                }
            }
            return "";
        }

        [HttpGet("[action]")]
        public string UpdateML()
        {
            _mlService.UpdateML();
            return "";
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

        public async void OpenWebSocketOnSymbol(string symbol)
        {
            _webSocket.ws = new MarketDataWebSocket($"{symbol.ToLower()}@kline_{interval}");
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
                await _webSocket.ws.ConnectAsync(CancellationToken.None);
                string message = await onlyOneMessage.Task;
                await _webSocket.ws.DisconnectAsync(CancellationToken.None);
            }
            catch
            {
                Console.WriteLine("impossible to open websocket on " + symbol);
            }
            finally
            {
                _webSocket.ws = new MarketDataWebSocket($"{symbol.ToLower()}@kline_{interval}");
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
                P = TradeHelper.CalculPourcentChange(stream.k.c, candleList, interval, 4),
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
            //We order the matrix with best coin to worst coin
            Global.candleMatrix = Global.candleMatrix.OrderByDescending(p => p.Last().MacdSlope).ToList();
        }

        public async Task<string> OpenWebSocketOnSpot()
        {
            //_webSocket.ws1 = new MarketDataWebSocket("!ticker@arr");
            _webSocket.ws1 = new MarketDataWebSocket("!ticker_4h@arr");
            //_webSocket.ws1 = new MarketDataWebSocket("!ticker_1h@arr");
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

                         nbrUp = buffer.Count(pred => pred.P >= 0);
                         nbrDown = buffer.Count(pred => pred.P < 0);

                         marketStreamOnSpot = buffer.Where(p => mySymbolList.Any(p1 => p1 == p.s)).OrderByDescending(p => p.P).ToList();

                         //Update db symbol table with new coins from Binance
                         if (Global.syncBinanceSymbol)
                         {
                             Global.syncBinanceSymbol = false;
                             ActionController actionController = new ActionController(_appDbContext);
                             //Update list of symbol from binance
                             actionController.UpdateDbSymbol(marketStreamOnSpot.Select(p => p.s).ToList());
                             //Update capitalisation and ranking from CoinMarketCap
                             actionController.UpdateCoinMarketCap();
                         }

                         _watchDog.Clear();

                         if (Global.isTradingOpen)
                         {
                             ProcessMarketMatrix();

                             for (int i = 0; i < Global.candleMatrix.Count; i++)
                             {
                                 for (int j = 0; j < Global.candleMatrix[i].Count; j++)
                                 {
                                     if (Global.candleMatrix[i][j].s != Global.candleMatrix[i][0].s)
                                     {
                                         Console.WriteLine("candles for " + Global.candleMatrix[i][0].s + " incoherante");
                                     }
                                 }
                             }
                         }
                     }
                     //return Task.CompletedTask;

                 }, CancellationToken.None);

            await _webSocket.ws1.ConnectAsync(CancellationToken.None);
            string message = await onlyOneMessage.Task;
            await _webSocket.ws1.DisconnectAsync(CancellationToken.None);

            return "";
        }

        #endregion

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////////-----------ALGORYTME----------/////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        #region Algo
        private void ProcessMarketMatrix()
        {
            try
            {
                //Review open orders
                foreach (var item in _repositoryService.GetActiveOrder())
                {
                    ReviewOpenTrade(item.Symbol);
                }

                if (_repositoryService.GetActiveOrder().Count < maxOpenTrade)
                {
                    foreach (var symbolCandelList in Global.candleMatrix.Take(10).ToList())
                    {
                        ReviewSpotMarket(marketStreamOnSpot, symbolCandelList);
                    }
                }

                _hub.Clients.All.SendAsync("trading", JsonSerializer.Serialize(marketStreamOnSpot));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ProcessMarketMatrix failled");
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

            var activeOrder = _repositoryService.GetActiveOrder().FirstOrDefault(p => p.Symbol == symbolSpot.s);
            var symbolCandle = Global.candleMatrix.Where(p => p.Last().s == symbolSpot.s).ToList().FirstOrDefault();
            var activeOrderCount = _repositoryService.GetActiveOrder().Count();

            if (activeOrder == null && activeOrderCount < maxOpenTrade)
            {
                //debug 
                //Console.WriteLine($"{symbolSpot.s} Spot24 : {symbolSpot.P} || {TradeHelper.CalculPourcentChange(symbolCandle, prevCandleCount)} calculated on last 2 candles ");

                if (EnterLongPosition(symbolSpot, symbolCandle))
                {
                    if (!Global.onHold.ContainsKey(symbolSpot.s))
                    {
                        Global.onHold.Add(symbolSpot.s, true);
                    }

                    if (Global.onAir)
                    {
                        Console.WriteLine($"Opening trade on {symbolSpot.s}");
                        Buy(symbolSpot, symbolCandle);
                    }
                    else
                    {
                        Console.WriteLine($"Opening fake trade on {symbolSpot.s}");
                        BuyFack(symbolSpot, symbolCandle);
                    }
                }


                if (Global.swallowOneOrder == true && symbolSpot.s != "XRPUSDT")
                {
                    Global.swallowOneOrder = false;
                    Console.WriteLine($"Opening trade on {symbolSpot.s}");
                    Buy(symbolSpot, symbolCandle);
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

            if(symbolSpot.P <0)
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

            //&& symbolCandle.Last().Macd < 100
        }

        private void ReviewOpenTrade(string symbol)
        {
            //we have a problem with sync and killed method called even trade positive
            //Let's try with an external object
            // lock (Global.candleMatrix.ToList())  instead of that
            lock (candleMatrixLock)
            {
                var activeOrder = _repositoryService.GetActiveOrder().Where(p => p.Symbol == symbol).Select(p => p).FirstOrDefault();
                var symbolCandle = Global.candleMatrix.ToList().Where(p => p.First().s == symbol).Select(p => p.Last()).FirstOrDefault();
                var lastPrice = symbolCandle.c;
                var highPrice = symbolCandle.h;

                if (activeOrder != null)
                {
                    TimeSpan span = DateTime.Now.Subtract(DateTime.Parse(activeOrder.OpenDate));
                    if (activeOrder.HighPrice <= activeOrder.OpenPrice && span.TotalMinutes > 15)
                    {
                        if (Global.onAir)
                        {
                            Console.WriteLine("Close trade : stop lose ");
                            Sell(activeOrder.Id, "killed");
                            return;
                        }
                        else
                        {
                            Console.WriteLine("Close fake trade : stop lose ");
                            SellFack(activeOrder.Id, "killed", lastPrice);
                            return;
                        }
                    }

                    //Check stop lose
                    if (lastPrice < activeOrder.StopLose)
                    {
                        if (Global.onAir)
                        {
                            Console.WriteLine("Close trade : stop lose ");
                            Sell(activeOrder.Id, "stop lose");
                            return;
                        }
                        else
                        {
                            Console.WriteLine("Close fake trade : stop lose ");
                            SellFack(activeOrder.Id, "stop lose", lastPrice);
                            return;
                        }
                    }

                    //take profit
                    if (lastPrice <= activeOrder.TakeProfit && lastPrice > activeOrder.OpenPrice)
                    {
                        if (Global.onAir)
                        {
                            Console.WriteLine("Close trade : take profit");
                            Sell(activeOrder.Id, "Take profit");
                            return;
                        }
                        else
                        {
                            Console.WriteLine("Close fake trade : take profit");
                            SellFack(activeOrder.Id, "Take profit", lastPrice);
                            return;
                        }
                    }

                    //AI close
                    if (_mlService.MLPredList.ToList().Where(p => p.Symbol == activeOrder.Symbol).Select(p => p.PredictedLabel).FirstOrDefault() == "down"
                     && _mlService.MLPredList.ToList().Where(p => p.Symbol == activeOrder.Symbol).Select(p => p.Score[0]).FirstOrDefault() >= 0.97)
                    {
                        if (Global.onAir)
                        {
                            Console.WriteLine("Close trade : AI take profit ");
                            Sell(activeOrder.Id, "AI sold");
                            return;
                        }
                        else
                        {
                            Console.WriteLine("Close fake trade : AI take profit ");
                            SellFack(activeOrder.Id, "AI sold", lastPrice);
                            return;
                        }
                    }

                    _repositoryService.UpdateTakeProfit(Global.candleMatrix.ToList().Where(p => p.Last().s == symbol).FirstOrDefault(), activeOrder, takeProfitPercentage);

                    _repositoryService.UpdateStopLoss(Global.candleMatrix.ToList().Where(p => p.Last().s == symbol).FirstOrDefault(), activeOrder);

                    _repositoryService.SaveHighLow(Global.candleMatrix.ToList().Where(p => p.Last().s == symbol).FirstOrDefault(), activeOrder);
                }
            }
        }



        private bool IsShort(MarketStream symbolSpot, List<Candle> symbolCandle)
        {
            return true;
        }

        #endregion

        #region Buy / Sell

        private async void Buy(MarketStream symbolSpot, List<Candle> symbolCandleList)
        {
            System.Net.HttpStatusCode httpStatusCode = System.Net.HttpStatusCode.NoContent;
            BinanceOrder myBinanceOrder = _binanceService.BuyMarket(symbolSpot.s, quoteOrderQty, ref httpStatusCode);

            //if order not executed we try divide amount by 2 and 3
            if (httpStatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                myBinanceOrder = _binanceService.BuyMarket(symbolSpot.s, quoteOrderQty / 2, ref httpStatusCode);
                if (httpStatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    myBinanceOrder = _binanceService.BuyMarket(symbolSpot.s, quoteOrderQty / 3, ref httpStatusCode);
                    if (httpStatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        _logger.LogWarning($"Call {MyEnum.BinanceApiCall.BuyMarket} {symbolSpot.s} Locked");
                        return;
                    }
                }
            }

            if (myBinanceOrder == null)
            {
                Global.onHold.Remove(symbolSpot.s);
                return;
            }

            if (myBinanceOrder.status == "EXPIRED")
            {
                await _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.BinanceSellOrderExpired.ToString());
                _logger.LogWarning($"Call {MyEnum.BinanceApiCall.BuyMarket} {symbolSpot.s} Expired");
                Global.onHold.Remove(symbolSpot.s);
                return;

            }
            int i = 0;
            while (myBinanceOrder.status != "FILLED")
            {
                if (i == 5) break;
                myBinanceOrder = _binanceService.OrderStatus(myBinanceOrder.symbol, myBinanceOrder.orderId);
                i++;
            }

            if (myBinanceOrder.status == "FILLED")
            {
                //Save here binance order result in db
                SaveTrade(symbolSpot, symbolCandleList, myBinanceOrder);
                Global.onHold.Remove(symbolSpot.s);
                await _hub.Clients.All.SendAsync("newPendingOrder", JsonSerializer.Serialize(myBinanceOrder));
                await Task.Delay(500);
                await _hub.Clients.All.SendAsync("newOrder");
            }
        }

        private async void Sell(double id, string closeType)
        {
            System.Net.HttpStatusCode httpStatusCode = System.Net.HttpStatusCode.NoContent;

            Order myOrder = _appDbContext.Order.Where(p => p.Id == id).Select(p => p).FirstOrDefault();
            BinanceOrder myBinanceOrder = _binanceService.SellMarket(myOrder.Symbol, myOrder.Quantity, ref httpStatusCode);

            if (myBinanceOrder == null) return;

            int i = 0;
            while (myBinanceOrder.status != "FILLED")
            {
                if (i == 5) break;
                myBinanceOrder = _binanceService.OrderStatus(myBinanceOrder.symbol, myBinanceOrder.orderId);
                i++;
            }

            if (myBinanceOrder.status == "FILLED")
            {
                CloseTrade(id, closeType, myBinanceOrder);
                await Task.Delay(500);
                await _hub.Clients.All.SendAsync("sellOrderFilled", JsonSerializer.Serialize(myBinanceOrder));
            }
        }

        private void SaveTrade(MarketStream symbolSpot, List<Candle> symbolCandle, BinanceOrder binanceOrder)
        {
            Console.WriteLine("Open trade");

            //Debug : something wrong with path of model on mac
            //List<ModelOutput> prediction = AIHelper.GetPrediction(symbolCandle);

            Order myOrder = new Order();
            myOrder.OpenDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            myOrder.OpenPrice = Helper.CalculateAvragePrice(binanceOrder);
            myOrder.HighPrice = 0;
            myOrder.LowPrice = Helper.CalculateAvragePrice(binanceOrder);
            myOrder.ClosePrice = 0;
            myOrder.Volume = symbolSpot.v;
            myOrder.TakeProfit = Helper.CalculateAvragePrice(binanceOrder) * (1 - (takeProfitPercentage / 100));
            myOrder.StopLose = Helper.CalculateAvragePrice(binanceOrder) * (1 - (stopLossPercentage / 100));
            myOrder.Quantity = Helper.ToDouble(binanceOrder.executedQty);
            myOrder.IsClosed = 0;
            myOrder.Fee = Global.isProd ? binanceOrder.fills.Sum(p => Helper.ToDouble(p.commission)) : Math.Round((Helper.CalculateAvragePrice(binanceOrder) * Helper.ToDouble(binanceOrder.executedQty)) / 100) * 0.1;
            myOrder.Symbol = binanceOrder.symbol;
            myOrder.MLBuyScore = _mlService.MLPredList.ToList().Where(p => p.Symbol == binanceOrder.symbol).Select(p => p.Score[1]).FirstOrDefault();
            myOrder.ATR = symbolCandle.Last().ATR;

            myOrder.RSI = symbolCandle.Last().Rsi;
            myOrder.RSI_1 = symbolCandle[symbolCandle.Count - 2].Rsi;
            myOrder.RSI_2 = symbolCandle[symbolCandle.Count - 3].Rsi;
            myOrder.EMA = symbolCandle.Last().Ema;
            myOrder.StochSlowD = symbolCandle.Last().StochSlowD;
            myOrder.StochSlowK = symbolCandle.Last().StochSlowK;
            myOrder.MACD = symbolCandle.Last().Macd;
            myOrder.MACDSign = symbolCandle.Last().MacdSign;
            myOrder.MACDHist = symbolCandle.Last().MacdHist;
            myOrder.MACDHist_1 = symbolCandle[symbolCandle.Count - 2].MacdHist;
            myOrder.MACDHist_2 = symbolCandle[symbolCandle.Count - 3].MacdHist;
            myOrder.MACDHist_3 = symbolCandle[symbolCandle.Count - 4].MacdHist;
            // myOrder.PredictionLBFGS = prediction[0].Prediction == true ? 1 : 0;
            // myOrder.PredictionLDSVM = prediction[1].Prediction == true ? 1 : 0;
            // myOrder.PredictionSDA = prediction[2].Prediction == true ? 1 : 0;

            myOrder.Lock = 0;
            myOrder.MarketTrend = $"{nbrUp}|{nbrDown}";
            myOrder.Status = "FILLED";
            myOrder.OrderId = binanceOrder.orderId;

            _appDbContext.Order.Add(myOrder);
            _appDbContext.SaveChanges();
        }

        private void CloseTrade(double orderId, string closeType, BinanceOrder binanceOrder)
        {
            Order myOrder = _appDbContext.Order.Where(p => p.Id == orderId).Select(p => p).FirstOrDefault();
            if (!Global.onHold.ContainsKey(myOrder.Symbol)) Global.onHold.Add(myOrder.Symbol, true);

            myOrder.ClosePrice = Helper.CalculateAvragePrice(binanceOrder);
            //myOrder.Fee += binanceOrder.fills.Sum(P => long.Parse(P.commission));
            myOrder.Profit = Math.Round((myOrder.ClosePrice - myOrder.OpenPrice) * myOrder.Quantity);
            myOrder.IsClosed = 1;
            myOrder.Type = closeType;
            myOrder.MLSellScore = _mlService.MLPredList.ToList().Where(p => p.Symbol == binanceOrder.symbol).Select(p => p.Score[0]).FirstOrDefault();
            myOrder.CloseDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            _appDbContext.SaveChanges();
        }

        #endregion

        #region Debug


        //for debugging
        //if (Globals.swallowOneOrder)
        //{
        //    Globals.swallowOneOrder = false;
        //    numberActiveOrder++;
        //    Console.WriteLine($"Open one Fack order on {Globals.candleMatrix[0].Last().s}");
        //    BuyFack(symbolOnSpot, symbolCandle);
        //}

        private async void BuyFack(MarketStream symbolSpot, List<Candle> symbolCandleList)
        {
            _logger.LogWarning($"BuyFack on {symbolSpot.s}");
            BinanceOrder fackBinanceOrder = new BinanceOrder();

            lock (symbolCandleList)
            {

                fackBinanceOrder.symbol = symbolSpot.s;
                fackBinanceOrder.executedQty = (quoteOrderQty / symbolSpot.c).ToString();
                fackBinanceOrder.cummulativeQuoteQty = quoteOrderQty.ToString();
                fackBinanceOrder.status = "FACK!";
                fackBinanceOrder.price = symbolSpot.c.ToString();
                fackBinanceOrder.side = "BUY";
                fills theOrderDetail = new fills();
                theOrderDetail.price = symbolSpot.c.ToString();
                theOrderDetail.qty = (quoteOrderQty / symbolSpot.c).ToString();
                fackBinanceOrder.fills = new List<fills>();
                fackBinanceOrder.fills.Add(theOrderDetail);

                SaveTrade(symbolSpot, symbolCandleList, fackBinanceOrder);
                Global.onHold.Remove(symbolSpot.s);
            }
            await _hub.Clients.All.SendAsync("newPendingOrder", JsonSerializer.Serialize(fackBinanceOrder));
            await Task.Delay(500);
            await _hub.Clients.All.SendAsync("newOrder");
        }

        private async void SellFack(double id, string closeType, double lastPrice)
        {
            Order myOrder = _appDbContext.Order.Where(p => p.Id == id).Select(p => p).FirstOrDefault();
            _logger.LogWarning($"SellFack on {myOrder.Symbol}");

            BinanceOrder fackBinanceOrder = new BinanceOrder();
            fackBinanceOrder.symbol = myOrder.Symbol;
            //fackBinanceOrder.executedQty = (quoteOrderQty / lastPrice).ToString();
            fackBinanceOrder.cummulativeQuoteQty = (myOrder.Quantity * lastPrice).ToString();
            fackBinanceOrder.status = "FACK!";
            fackBinanceOrder.price = lastPrice.ToString();
            fackBinanceOrder.side = "SELL";
            fills theOrderDetail = new fills();
            theOrderDetail.price = lastPrice.ToString();
            theOrderDetail.qty = (quoteOrderQty / lastPrice).ToString();
            fackBinanceOrder.fills = new List<fills>();
            fackBinanceOrder.fills.Add(theOrderDetail);

            CloseTrade(id, closeType, fackBinanceOrder);
            await Task.Delay(500);
            await _hub.Clients.All.SendAsync("sellOrderFilled", JsonSerializer.Serialize(fackBinanceOrder));

        }

        [HttpGet("[action]")]
        public void TestBinanceBuy()
        {
            Global.swallowOneOrder = true;
            //Symbol + USDT amount
            //var ttt = BinanceHelper.OrderStatus("ETHUSDT", 123);
            // BinanceHelper.BuyMarket("ETHUSDT", 100);
        }
        #endregion
    }
}
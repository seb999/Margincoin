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
using System.Linq;
using System.Net.WebSockets;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tensorflow;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AutoTrade3Controller : ControllerBase
    {

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////------------Global varibles----------//////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        #region Global variables

        MarketDataWebSocket ws = new MarketDataWebSocket("define later in the code");
        MarketDataWebSocket ws1 = new MarketDataWebSocket("define later in the code");

        private IHubContext<SignalRHub> _hub;
        private IBinanceService _binanceService;
        private IMLService _mlService;
        private IWatchDog _watchDog;
        private readonly ApplicationDbContext _appDbContext;
        private ILogger _logger;
        private List<List<Candle>> candleMatrix = new List<List<Candle>>();
        private List<MarketStream> buffer = new List<MarketStream>();
        private List<MarketStream> marketStreamOnSpot = new List<MarketStream>();
        private List<string> mySymbolList = new List<string>();
        int nbrUp = 0;
        int nbrDown = 0;

        #endregion  

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////------------SETTINGS----------/////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        #region Settings    

        private readonly string interval = "1h";   //1h seem to give better result
        private readonly string maxCandle = "100";
        private readonly int prevCandleCount = 2;
        private readonly double stopLossPercentage = 1.2;
        private readonly double takeProfit = 1;
        private readonly int maxOpenTrade = 2;
        //How many hours we look back 
        private readonly int backTimeHours = 4;
        //Max amount to invest for each trade
        private readonly int quoteOrderQty = 3000;
        //Select short list of symbol or full list(on test server on 6 symbols allowed)
        private readonly bool fullSymbolList = true;

        #endregion

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        ///////////////////////////////-----------Constructor----------/////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        #region Constructor

        public AutoTrade3Controller(
            IHubContext<SignalRHub> hub,
            [FromServices] ApplicationDbContext appDbContext,
            ILogger<AutoTrade3Controller> logger,
            IBinanceService binanceService,
            IMLService mLService,
            IWatchDog watchDog)
        {
            _hub = hub;
            _binanceService = binanceService;
            _appDbContext = appDbContext;
            _logger = logger;
            _mlService = mLService;
            _watchDog = watchDog;

            //For futur reference between controllers
            Globals.fullSymbolList = fullSymbolList;

            //Get the list of symbol to trade from DB
            mySymbolList = GetSymbolList();

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

                await ws.DisconnectAsync(CancellationToken.None);
                await ws1.DisconnectAsync(CancellationToken.None);
                _logger.LogWarning($"Whatchdog kill all websock and restart it");
            }

            //open a webSocket for each symbol in my list
            foreach (var symbol in mySymbolList)
            {
                _binanceService.GetCandles(symbol, ref candleMatrix);
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
                if (Globals.onAir)
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
        public List<string> GetSymbolList()
        {
            if (fullSymbolList)
            {
                return _appDbContext.Symbol.Where(p => p.IsOnProd != 0).Select(p => p.SymbolName).ToList();
            }
            else
            {
                return _appDbContext.Symbol.Where(p => p.IsOnTest != 0).Select(p => p.SymbolName).ToList();
            }
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

        private async void OpenWebSocketOnSymbol(string symbol)
        {
            ws = new MarketDataWebSocket($"{symbol.ToLower()}@kline_{interval}");
            var onlyOneMessage = new TaskCompletionSource<string>();
            

            ws.OnMessageReceived(
                (data) =>
                {
                    data = data.Remove(data.IndexOf("}}") + 2);
                    var stream = Helper.deserializeHelper<StreamData>(data);
                    var symbolIndex = 0;

                    //get corresponding line in our Matrice
                    for (int i = 0; i < candleMatrix.Count; i++)
                    {
                        if (candleMatrix[i][0].s == stream.k.s)
                        {
                            symbolIndex = i;
                            break;
                        }
                    }

                    if (!stream.k.x)
                    {
                        if (candleMatrix[symbolIndex].Count > 0) candleMatrix[symbolIndex] = candleMatrix[symbolIndex].SkipLast(1).ToList();
                    }
                    else
                    {
                        Console.WriteLine($"New candle save : {stream.k.s}");
                        _logger.LogWarning($"New candle save : {stream.k.s}");
                        Globals.onHold.Remove(stream.k.s);
                    }

                    Candle newCandle = new Candle()
                    {
                        s = stream.k.s,
                        o = stream.k.o,
                        h = stream.k.h,
                        l = stream.k.l,
                        c = stream.k.c,
                        P = TradeHelper.CalculPourcentChange(stream, candleMatrix[symbolIndex].ToList(), interval, backTimeHours),
                    };
                    candleMatrix[symbolIndex].Add(newCandle);
                    List<Candle> candleListWithIndicators = TradeIndicator.CalculateIndicator(candleMatrix[symbolIndex]);
                    candleMatrix[symbolIndex] = candleListWithIndicators;

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
            ws1 = new MarketDataWebSocket("!ticker@arr");
           // ws1 = new MarketDataWebSocket("!ticker_4h@arr");
            var onlyOneMessage = new TaskCompletionSource<string>();
            string dataResult = "";

            ws1.OnMessageReceived(
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

                        marketStreamList = marketStreamList
                            .Where(p => p.s.Contains("USDT"))
                            .ToList();
                        
                        TradeHelper.BufferMarketStream(marketStreamList, ref buffer);

                        nbrUp = buffer.Count(pred => pred.P >= 0);
                        nbrDown = buffer.Count(pred => pred.P < 0);

                        marketStreamOnSpot = buffer
                            .Where(p => mySymbolList.Any(p1 => p1 == p.s))
                            .OrderByDescending(p => p.P).ToList();

                        _watchDog.Clear();

                        if (Globals.isTradingOpen)
                        {
                            ProcessMarketMatrix();
                        }
                    }
                    //return Task.CompletedTask;

                }, CancellationToken.None);

            await ws1.ConnectAsync(CancellationToken.None);
            string message = await onlyOneMessage.Task;
            await ws1.DisconnectAsync(CancellationToken.None);

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
                foreach (var symbolSpot in marketStreamOnSpot.ToList())
                {
                    //Terminate active position SHORT or LONG if needed
                    ActiveTradeReview(symbolSpot.s, candleMatrix.ToList());

                    //Open new position SHORT or LONG if positive signal
                    SymbolSpotReview(symbolSpot, candleMatrix.ToList());

                    //Send last data to frontend
                    _hub.Clients.All.SendAsync("trading", JsonSerializer.Serialize(marketStreamOnSpot));
                }
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

        private void SymbolSpotReview(MarketStream symbolSpot, List<List<Candle>> candleMatrix)
        {
            
            var activeOrder = GetActiveOrder().FirstOrDefault(p => p.Symbol == symbolSpot.s);
            var symbolCandle = candleMatrix.Where(p => p.Last().s == symbolSpot.s).FirstOrDefault();
            var activeOrderCount = GetActiveOrder().Count();

            //debug 
            Console.WriteLine($"{symbolSpot.s} Spot24 : {symbolSpot.P} || {symbolCandle.Last().P} calculated on {this.backTimeHours}H ");

            if (activeOrder == null && activeOrderCount < maxOpenTrade)
            {
                if (symbolSpot.P > 0 && EnterLongPosition(symbolSpot, symbolCandle))
                {
                    if (!Globals.onHold.ContainsKey(symbolSpot.s))
                    {
                        Globals.onHold.Add(symbolSpot.s, true);
                    }

                    if (Globals.onAir)
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

                if(symbolSpot.P < 0 && IsShort(symbolSpot, symbolCandle))
                {

                }
            }
        }

        private void ActiveTradeReview(string symbol, List<List<Candle>> myCandleMatrice)
        {
            var activeOrder = GetActiveOrder().Where(p => p.Symbol == symbol).Select(p => p).FirstOrDefault();
            var symbolCandle = myCandleMatrice.ToList().Where(p => p.First().s == symbol).FirstOrDefault();
            var lastPrice = symbolCandle.Select(p => p.c).LastOrDefault();
            var highPrice = symbolCandle.Select(p => p.h).LastOrDefault();
            var lastCandle = symbolCandle.Select(p => p).LastOrDefault();

            if (activeOrder != null)
            {
                SaveHighLow(lastCandle, activeOrder);

                //Check stop lose
                if (lastPrice <= (activeOrder.StopLose))
                {
                    if (Globals.onAir)
                    {
                        Console.WriteLine("Close trade : stop lose ");
                        Sell(activeOrder.Id, "stop lose");
                    }
                    else
                    {
                        Console.WriteLine("Close fake trade : stop lose ");
                        SellFack(activeOrder.Id, "stop lose", lastPrice);
                    }
                }

                //After +2% we use a take profit
                if (lastPrice > activeOrder.OpenPrice * 1.02)
                {
                    if (lastPrice <= (activeOrder.HighPrice * (1 - (activeOrder.TakeProfit / 100))))
                    {
                        if (Globals.onAir)
                        {
                            Console.WriteLine("Close trade : take profit");
                            Sell(activeOrder.Id, "Take profit");
                        }
                        else
                        {
                            Console.WriteLine("Close fake trade : take profit");
                            SellFack(activeOrder.Id, "Take profit", lastPrice);
                        }
                    }
                }

                //AI close
                if (_mlService.MLPredList.ToList().Where(p => p.Symbol == activeOrder.Symbol).Select(p => p.PredictedLabel).FirstOrDefault() == "down"
                 && _mlService.MLPredList.ToList().Where(p => p.Symbol == activeOrder.Symbol).Select(p => p.Score[0]).FirstOrDefault() >= 0.85)
                {
                    if (Globals.onAir)
                    {
                        Console.WriteLine("Close trade : AI take profit ");
                        Sell(activeOrder.Id, "AI sold");
                    }
                    else
                    {
                        Console.WriteLine("Close fake trade : AI take profit ");
                        SellFack(activeOrder.Id, "AI sold", lastPrice);
                    }
                }

                //UpdateStopLose(lastPrice, activeOrder);

                //UpdateTakeProfit(lastPrice, activeOrder);

                SecureProfit(symbolCandle, activeOrder);
            }
        }

        private bool EnterLongPosition(MarketStream symbolSpot, List<Candle> symbolCandles)
        {
            const int MIN_CONSECUTIVE_UP_SYMBOL = 30;
            const int MAX_SPREAD = -5;
            const double MIN_SCORE = 0.70;
            const double MIN_RSI = 40;
            const double MAX_RSI = 82;

            bool isLong = true;

            if (nbrUp < MIN_CONSECUTIVE_UP_SYMBOL && symbolSpot.P > MAX_SPREAD)
            {
                isLong = false;
            }

            // Check if there are enough candles to perform the analysis
            if (symbolCandles.Count > 2)
            {
                for (int i = symbolCandles.Count - prevCandleCount; i < symbolCandles.Count; i++)
                {
                    if ((TradeHelper.CandleColor(symbolCandles[i]) != "green" || symbolCandles[i].c <= symbolCandles[i - 1].c))
                    {
                        isLong = false;
                        break;
                    }
                }
            }

            var mlPrediction = _mlService.MLPredList.FirstOrDefault(p => p.Symbol == symbolSpot.s);
            if (mlPrediction == null || mlPrediction.PredictedLabel != "up" || mlPrediction.Score[1] < MIN_SCORE)
            {
                isLong = false;
            };

            if (Globals.onHold.ContainsKey(symbolSpot.s) && Globals.onHold[symbolSpot.s])
            {
                isLong = false;
            };


            if(symbolCandles.Last().Rsi < MIN_RSI || symbolCandles.Last().Rsi > MAX_RSI)
            {
                isLong = false;
            };
            return isLong;

            //&& symbolCandle.Last().Macd < 100
        }

        private bool IsShort(MarketStream symbolSpot, List<Candle> symbolCandle)
        { 
            return true; 
        }

        private void SecureProfit(List<Candle> symbolCandle, Order activeOrder)
        {
            double currentPrice = symbolCandle.Select(p => p.c).LastOrDefault();
            double trend = currentPrice - activeOrder.OpenPrice;
            double stopLoss = activeOrder.OpenPrice - (activeOrder.OpenPrice * (stopLossPercentage / 100));
            
            if (trend > 0 && TradeHelper.CandleColor(symbolCandle.Last()) == "green")
            {
                // Trend is going up, increase stop-loss to follow the trend
                stopLoss = currentPrice * (1 - ((stopLossPercentage-0.6) / 100));
                activeOrder.StopLose = stopLoss;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }
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
                Globals.onHold.Remove(symbolSpot.s);
                return;
            }

            if (myBinanceOrder.status == "EXPIRED")
            {
                await _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.BinanceSellOrderExpired.ToString());
                _logger.LogWarning($"Call {MyEnum.BinanceApiCall.BuyMarket} {symbolSpot.s} Expired");
                Globals.onHold.Remove(symbolSpot.s);
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
                Globals.onHold.Remove(symbolSpot.s);
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
            myOrder.HighPrice = Helper.CalculateAvragePrice(binanceOrder);
            myOrder.LowPrice = Helper.CalculateAvragePrice(binanceOrder);
            myOrder.Volume = symbolSpot.v;
            myOrder.TakeProfit = takeProfit;
            myOrder.StopLose = Helper.CalculateAvragePrice(binanceOrder) * (1 - (stopLossPercentage / 100));
            myOrder.Quantity = Helper.ToDouble(binanceOrder.executedQty);
            myOrder.IsClosed = 0;
            myOrder.Fee = Globals.isProd ? binanceOrder.fills.Sum(p => Helper.ToDouble(p.commission)) : Math.Round((Helper.CalculateAvragePrice(binanceOrder) * Helper.ToDouble(binanceOrder.executedQty)) / 100) * 0.1;
            myOrder.Symbol = binanceOrder.symbol;

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
            if (!Globals.onHold.ContainsKey(myOrder.Symbol)) Globals.onHold.Add(myOrder.Symbol, true);

            myOrder.ClosePrice = Helper.CalculateAvragePrice(binanceOrder);
            //myOrder.Fee += binanceOrder.fills.Sum(P => long.Parse(P.commission));
            myOrder.Profit = Math.Round((myOrder.ClosePrice - myOrder.OpenPrice) * myOrder.Quantity) - myOrder.Fee;
            myOrder.IsClosed = 1;
            myOrder.Type = closeType;
            myOrder.CloseDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            _appDbContext.SaveChanges();
        }

        #endregion

        #region Helper

        private List<Order> GetActiveOrder()
        {
            return _appDbContext.Order.Where(p => p.IsClosed == 0).ToList();
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

        #region Debug


        //for debugging
        //if (Globals.swallowOneOrder)
        //{
        //    Globals.swallowOneOrder = false;
        //    numberActiveOrder++;
        //    Console.WriteLine($"Open one Fack order on {candleMatrix[0].Last().s}");
        //    BuyFack(symbolOnSpot, symbolCandle);
        //}

        private async void BuyFack(MarketStream symbolSpot, List<Candle> symbolCandleList)
        {
            _logger.LogWarning($"BuyFack on {symbolSpot.s}");
            BinanceOrder fackBinanceOrder = new BinanceOrder();
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
            Globals.onHold.Remove(symbolSpot.s);
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
            Globals.swallowOneOrder = true;
            //Symbol + USDT amount
            //var ttt = BinanceHelper.OrderStatus("ETHUSDT", 123);
            // BinanceHelper.BuyMarket("ETHUSDT", 100);
        }
        #endregion
    }
}
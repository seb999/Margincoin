using Binance.Spot;
using Binance.Net.Clients;
using MarginCoin.Class;
using MarginCoin.Misc;
using MarginCoin.Model;
using MarginCoin.Service;
using MarginCoin.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        #region Dependencies

        private readonly IHubContext<SignalRHub> _hub;
        private readonly IBinanceService _binanceService;
        private readonly IMLService _mlService;
        private readonly IWatchDog _watchDog;
        private readonly IWebSocket _webSocket;
        private readonly IOrderService _orderService;
        private readonly ISymbolService _symbolService;
        private readonly ITradingState _tradingState;
        private readonly TradingConfiguration _config;
        private readonly ApplicationDbContext _appDbContext;
        private readonly ILogger _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private List<MarketStream> buffer = new List<MarketStream>();
        private List<MarketStream> marketStreamOnSpot = new List<MarketStream>();
        int nbrUp = 0;
        int nbrDown = 0;
        int i = 0;

        private readonly object candleMatrixLock = new object();

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
            IWebSocket webSocket,
            ISymbolService symbolService,
            ITradingState tradingState,
            IOptions<TradingConfiguration> tradingConfig,
            IServiceScopeFactory serviceScopeFactory)
        {
            _hub = hub;
            _appDbContext = appDbContext;
            _logger = logger;
            _binanceService = binanceService;
            _orderService = orderService;
            _mlService = mLService;
            _watchDog = watchDog;
            _webSocket = webSocket;
            _symbolService = symbolService;
            _tradingState = tradingState;
            _config = tradingConfig.Value;
            _serviceScopeFactory = serviceScopeFactory;

            // Configure binance service
            _binanceService.Interval = _config.Interval;
            _binanceService.Limit = _config.MaxCandle;

            // Initialize trading state
            _tradingState.SyncBinanceSymbol = false;

            // Initialize symbol lists if empty
            if (_tradingState.SymbolWeTrade.Count == 0)
            {
                _tradingState.SymbolWeTrade = _symbolService.GetTradingSymbols();
                _tradingState.SymbolBaseList = _symbolService.GetBaseSymbols();
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

            //ML init and watchdog init
            // if (!_watchDog.IsWebsocketSpotDown)
            // {
            //     _mlService.InitML(UpdateSymbolWeTrade);
            //     _watchDog.InitWatchDog(RestartWebSocket);
            // }
            // else
            // {
            //     // Disconnect all symbol websockets
            //     foreach (var ws in _webSocket.SymbolWebSockets.Values)
            //     {
            //         await ws.DisconnectAsync(CancellationToken.None);
            //     }
            //     _webSocket.ClearSymbolWebSockets();
            //     await _webSocket.ws1.DisconnectAsync(CancellationToken.None);
            //     _tradingState.ClearCandleMatrix();
            //     _logger.LogWarning("Watchdog kill all websockets and restart it");
            // }

            //open a webSocket for each symbol in my list
            foreach (var symbol in _tradingState.SymbolWeTrade)
            {
                await OpenWebSocketOnSymbol(symbol);
            }

            //add open order symbol not in SymbolWeTrade list
            foreach (var order in _orderService.GetActiveOrder())
            {
                if (_tradingState.SymbolWeTrade.SingleOrDefault(p => p.SymbolName == order.Symbol) == null)
                {
                    var orderSymbol = _tradingState.SymbolBaseList.SingleOrDefault(p => p.SymbolName == order.Symbol);
                    await OpenWebSocketOnSymbol(orderSymbol);
                    _tradingState.SymbolWeTrade.Add(orderSymbol);
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
                if (_tradingState.IsMarketOrder == true)
                    _orderService.SellMarket(myOrder, "by user");
                else
                    _orderService.SellLimit(myOrder, _tradingState.MarketStreamOnSpot.SingleOrDefault(p => p.s == myOrder.Symbol), "by user");
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
            _tradingState.SyncBinanceSymbol = true;
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

        public async Task OpenWebSocketOnSymbol(Symbol symbol)
        {
            // Check if WebSocket already exists for this symbol
            if (_webSocket.HasSymbolWebSocket(symbol.SymbolName))
            {
                _logger.LogWarning($"WebSocket already exists for {symbol.SymbolName}");
                return;
            }

            var candleMatrix = _tradingState.CandleMatrix;
            _binanceService.GetCandles(symbol.SymbolName, ref candleMatrix);

            var ws = new MarketDataWebSocket($"{symbol.SymbolName.ToLower()}@kline_{_config.Interval}");

            ws.OnMessageReceived(
                (data) =>
                {
                    data = data.Remove(data.IndexOf("}}") + 2);
                    var stream = Helper.deserializeHelper<StreamData>(data);

                    //get corresponding line in our Matrice
                    for (int i = 0; i < _tradingState.CandleMatrix.Count; i++)
                    {
                        if (_tradingState.CandleMatrix[i][0].s == stream.k.s)
                        {
                            UpdateMatrix(stream, _tradingState.CandleMatrix[i]);
                            break;
                        }
                    }

                    return Task.CompletedTask;

                }, CancellationToken.None);

            try
            {
                await ws.ConnectAsync(CancellationToken.None);
                _webSocket.AddSymbolWebSocket(symbol.SymbolName, ws);
                _logger.LogWarning($"WebSocket opened for {symbol.SymbolName}");

                // Notify frontend of WebSocket connection
                await _hub.Clients.All.SendAsync("websocketStatus", JsonSerializer.Serialize(new
                {
                    symbol = symbol.SymbolName,
                    status = "connected",
                    totalConnections = _webSocket.SymbolWebSockets.Count
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to open websocket on {symbol.SymbolName}");
            }
        }

        private void UpdateMatrix(StreamData stream, List<Candle> candleList)
        {
            // Validate inputs
            if (stream?.k == null || candleList == null) return;

            lock (candleMatrixLock)
            {
                Candle newCandle = new()
                {
                    s = stream.k.s,
                    o = stream.k.o,
                    h = stream.k.h,
                    l = stream.k.l,
                    c = stream.k.c,
                    P = TradeHelper.CalculPourcentChange(stream.k.c, candleList, _config.Interval, 2),
                };

                // Update or add candle
                if (!stream.k.x)  // Candle not closed - update last
                {
                    if (candleList.Count == 0) return;
                    candleList[^1] = newCandle;  // Replace in-place
                }
                else  // Candle closed - add new
                {
                    candleList.Add(newCandle);
                    _logger.LogInformation("New candle saved: {Symbol}", stream.k.s);
                    _tradingState.OnHold.Remove(stream.k.s);
                }

                // Calculate RSI / MACD / EMA - modifies list in-place
                TradeIndicator.CalculateIndicator(candleList);

                // Calculate the slope of the MACD historic (derivative)
                if (candleList.Count > 0)
                {
                    candleList[^1].MacdSlope =
                        TradeHelper.CalculateMacdSlope(candleList, _config.Interval).Slope;
                }

                // Note: candleList is already a reference to the list in _tradingState.CandleMatrix
                // so modifications are already persisted. No need to replace.

                // Only re-sort when candle closes (not on every tick)
                if (stream.k.x)
                {
                    var orderedMatrix = _tradingState.CandleMatrix
                        .OrderByDescending(p => p.Count > 0 ? p[^1].P : 0)
                        .ToList();

                    _tradingState.CandleMatrix.Clear();
                    foreach (var item in orderedMatrix)
                    {
                        _tradingState.CandleMatrix.Add(item);
                    }

                    // Notify frontend of candle update
                    _hub.Clients.All.SendAsync("candleUpdate", JsonSerializer.Serialize(new
                    {
                        symbol = stream.k.s,
                        price = stream.k.c,
                        change = newCandle.P
                    }));
                }
            }
        }

        public async Task OpenWebSocketOnSpot()
        {
            // Check if already connected
            if (_webSocket.ws1 != null)
            {
                _logger.LogWarning("Spot WebSocket already exists");
                return;
            }

            _logger.LogWarning("Creating Spot WebSocket with stream: {Stream}", _config.SpotTickerTime);
            _webSocket.ws1 = new MarketDataWebSocket(_config.SpotTickerTime);
            var dataResult = new System.Text.StringBuilder();

            _webSocket.ws1.OnMessageReceived(
                 (data) =>
                 {
                     _logger.LogWarning("!!!CALLBACK TRIGGERED!!! Data: {Data}", data?.Substring(0, Math.Min(100, data?.Length ?? 0)));
                     _logger.LogDebug("Spot WebSocket received data, length: {Length}", data?.Length ?? 0);
                     dataResult.Append(data);
                     var fullData = dataResult.ToString();
                     _logger.LogDebug("Full data length: {Length}, Contains }]: {Contains}", fullData.Length, fullData.Contains("}]"));

                     if (fullData.Contains("}]"))
                     {
                         if (fullData.Length > (fullData.IndexOf("]") + 1))
                         {
                             fullData = fullData[..(fullData.IndexOf("]") + 1)];
                         }

                         List<MarketStream> marketStreamList = Helper.deserializeHelper<List<MarketStream>>(fullData);
                         dataResult.Clear();  //we clean it immediately to avoid a bug on new data coming

                         marketStreamList = marketStreamList.Where(p => p.s.Contains("USDT")).ToList();

                         TradeHelper.BufferMarketStream(marketStreamList, ref buffer);

                         //Update db symbol table with new coins from Binance
                         if (_tradingState.SyncBinanceSymbol)
                         {
                             _tradingState.SyncBinanceSymbol = false;
                             //Update list of symbol from binance
                             _symbolService.SyncBinanceSymbols(buffer.Select(p => p.s).ToList());
                             //Update capitalisation and ranking from CoinMarketCap
                             _symbolService.UpdateCoinMarketCap();
                         }

                         nbrUp = buffer.Count(pred => pred.P >= 0);
                         nbrDown = buffer.Count(pred => pred.P < 0);

                         marketStreamOnSpot = buffer.Where(p => _tradingState.SymbolBaseList.Any(p1 => p1.SymbolName == p.s))
                             .OrderByDescending(p => p.P)
                             .ToList();

                         _tradingState.MarketStreamOnSpot = marketStreamOnSpot;

                         _watchDog.Clear();

                         if (_tradingState.IsTradingOpen)
                         {
                             // Fire and forget - don't await in callback
                             _ = ProcessMarketMatrix();
                         }
                         else
                         {
                             _logger.LogDebug("Market data received but trading is not open. Enable trading to process market matrix.");
                         }
                     }

                     return Task.CompletedTask;

                 }, CancellationToken.None);

            try
            {
                await _webSocket.ws1.ConnectAsync(CancellationToken.None);
                _logger.LogWarning("Spot WebSocket connected - continuous streaming active");
                _logger.LogWarning("Spot WebSocket listening on stream: {Stream}", _config.SpotTickerTime);
                _logger.LogWarning("Waiting for data from Binance...");

                // Notify frontend of Spot WebSocket connection
                await _hub.Clients.All.SendAsync("websocketStatus", JsonSerializer.Serialize(new
                {
                    symbol = "SPOT",
                    status = "connected",
                    message = "Market data streaming active"
                }));

                // Keep connection alive - do NOT disconnect
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open Spot WebSocket");
                _webSocket.ws1 = null;
            }
        }

        public async Task UpdateSymbolWeTrade()
        {
            foreach (var symbol in marketStreamOnSpot.Take(3))
            {
                if (_tradingState.SymbolWeTrade.Where(p => p.SymbolName == symbol.s).FirstOrDefault() == null)
                {
                    Symbol hotSymbol = _tradingState.SymbolBaseList.Where(p => p.SymbolName == symbol.s).FirstOrDefault();
                    await OpenWebSocketOnSymbol(hotSymbol);
                    _tradingState.SymbolWeTrade.Add(hotSymbol);
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
            // Create a new scope for database operations called from WebSocket callbacks
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                try
                {
                    await ReviewPendingOrder(dbContext);

                    //Review open orders
                    foreach (var item in orderService.GetActiveOrder())
                    {
                        ReviewOpenTrade(marketStreamOnSpot, item.Symbol, orderService, dbContext);
                    }

                    if (orderService.GetActiveOrder().Count < _config.MaxOpenTrades)
                    {
                        foreach (var symbolCandelList in _tradingState.CandleMatrix.Take(10).ToList())
                        {
                            ReviewSpotMarket(marketStreamOnSpot, symbolCandelList, orderService);
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
        }

        private void ReviewSpotMarket(List<MarketStream> marketStreamList, List<Candle> symbolCandelList, IOrderService orderService)
        {
            var symbolSpot = marketStreamOnSpot.Where(p => p.s == symbolCandelList.Last().s).FirstOrDefault();

            if (symbolSpot == null)
            {
                return;
            }

            var activeOrder = orderService.GetActiveOrder().FirstOrDefault(p => p.Symbol == symbolSpot.s);
            var symbolCandle = _tradingState.CandleMatrix.Where(p => p.Last().s == symbolSpot.s).ToList().FirstOrDefault();
            var activeOrderCount = orderService.GetActiveOrder().Count();

            if (activeOrder == null && activeOrderCount < _config.MaxOpenTrades)
            {
                if (EnterLongPosition(symbolSpot, symbolCandle))
                {
                    if (!_tradingState.OnHold.ContainsKey(symbolSpot.s))
                    {
                        _tradingState.OnHold.Add(symbolSpot.s, true);
                    }

                    Console.WriteLine($"Opening trade on {symbolSpot.s}");

                    if (_tradingState.IsMarketOrder == true)
                    {
                        orderService.BuyMarket(symbolSpot, symbolCandle);
                    }
                    else
                    {
                        orderService.BuyLimit(symbolSpot, symbolCandle);
                    }
                }


                if (_tradingState.TestBuyLimit == true)
                {
                    _tradingState.TestBuyLimit = false;
                    Console.WriteLine($"Opening trade on {symbolSpot.s}");
                    orderService.BuyLimit(symbolSpot, symbolCandle);
                }

                if (symbolSpot.P < 0 && IsShort(symbolSpot, symbolCandle))
                {

                }
            }
        }

        private bool EnterLongPosition(MarketStream symbolSpot, List<Candle> symbolCandles)
        {
            // Basic validation
            if (symbolSpot.P < 0)
            {
                _logger.LogDebug("Entry rejected for {Symbol}: Negative price change {Change}%", symbolSpot.s, symbolSpot.P);
                return false;
            }

            // Market breadth check (with crash override)
            if (nbrUp < _config.MinConsecutiveUpSymbols && symbolSpot.P > _config.MaxSpreadOverride)
            {
                _logger.LogDebug("Entry rejected for {Symbol}: Market breadth too low ({Up} symbols up, need {Min})",
                    symbolSpot.s, nbrUp, _config.MinConsecutiveUpSymbols);
                return false;
            }

            // TREND SCORE - Primary filter
            var trendScore = TradeHelper.CalculateTrendScore(symbolCandles, _config.UseWeightedTrendScore);
            if (trendScore < _config.MinTrendScoreForEntry)
            {
                _logger.LogDebug("Entry rejected for {Symbol}: Trend score too low ({Score}, need {Min})",
                    symbolSpot.s, trendScore, _config.MinTrendScoreForEntry);
                return false;
            }

            // Check if there are enough candles to perform consecutive green candle analysis
            if (symbolCandles.Count > _config.PrevCandleCount)
            {
                // Check if previous candles are green (consecutive bullish candles)
                for (int i = symbolCandles.Count - _config.PrevCandleCount; i < symbolCandles.Count; i++)
                {
                    if (TradeHelper.CandleColor(symbolCandles[i]) != "green" || symbolCandles[i].c <= symbolCandles[i - 1].c)
                    {
                        _logger.LogDebug("Entry rejected for {Symbol}: Not enough consecutive green candles", symbolSpot.s);
                        return false;
                    }
                }

                // Strong movement validation - check percentage increase
                var percentChange = TradeHelper.CalculPourcentChange(symbolCandles, _config.PrevCandleCount);
                if (percentChange < _config.MinPercentageUp)
                {
                    _logger.LogDebug("Entry rejected for {Symbol}: Movement too weak ({Change}%, need {Min}%)",
                        symbolSpot.s, percentChange, _config.MinPercentageUp);
                    return false;
                }
            }

            // AI Veto - Only blocks if strongly bearish
            var mlPrediction = _mlService.MLPredList.ToList().FirstOrDefault(p => p.Symbol == symbolSpot.s);
            if (mlPrediction != null)
            {
                // Strong bearish signal from AI - veto the trade
                if (mlPrediction.PredictedLabel == "down" && mlPrediction.Score[0] >= _config.AIVetoConfidence)
                {
                    _logger.LogInformation("Entry vetoed by AI for {Symbol}: Predicted DOWN with {Confidence}% confidence",
                        symbolSpot.s, mlPrediction.Score[0] * 100);
                    return false;
                }

                // Optional: Weak AI confirmation (if available and predicts up, nice bonus but not required)
                if (mlPrediction.PredictedLabel == "up" && mlPrediction.Score[1] >= _config.MinAIScore)
                {
                    _logger.LogDebug("AI confirms entry for {Symbol} with {Confidence}% confidence",
                        symbolSpot.s, mlPrediction.Score[1] * 100);
                }
            }
            else
            {
                _logger.LogDebug("No ML prediction available for {Symbol}, proceeding with trend score only", symbolSpot.s);
            }

            // Check the symbol is not on hold
            if (_tradingState.OnHold.ContainsKey(symbolSpot.s) && _tradingState.OnHold[symbolSpot.s])
            {
                _logger.LogDebug("Entry rejected for {Symbol}: Symbol on hold", symbolSpot.s);
                return false;
            }

            // RSI boundaries check (avoid extreme overbought/oversold)
            var currentRsi = symbolCandles.Last().Rsi;
            if (currentRsi < _config.MinRSI || currentRsi > _config.MaxRSI)
            {
                _logger.LogDebug("Entry rejected for {Symbol}: RSI out of range ({RSI}, range: {Min}-{Max})",
                    symbolSpot.s, currentRsi, _config.MinRSI, _config.MaxRSI);
                return false;
            }

            // All checks passed
            _logger.LogInformation("✓ Entry signal for {Symbol}: Trend score={Score}, RSI={RSI}, Change={Change}%",
                symbolSpot.s, trendScore, currentRsi, symbolSpot.P);
            return true;
        }

        private void ReviewOpenTrade(List<MarketStream> marketStreamList, string symbol, IOrderService orderService, ApplicationDbContext dbContext)
        {
            lock (candleMatrixLock)
            {
                var activeOrder = orderService.GetActiveOrder().FirstOrDefault(p => p.Symbol == symbol);
                var symbolCandleList = _tradingState.CandleMatrix.FirstOrDefault(p => p.Last().s == symbol);
                var symbolCandle = symbolCandleList?.Last();
                var lastPrice = symbolCandle?.c;
                var highPrice = symbolCandle?.h;

                if (activeOrder == null || symbolCandle == null || symbolCandleList == null)
                    return;

                // Extracted method for logging and selling
                void LogAndSell(string message)
                {
                    _logger.LogInformation("Closing trade for {Symbol}: {Reason}", symbol, message);
                    if (_tradingState.IsMarketOrder)
                    {
                        orderService.SellMarket(activeOrder, message);
                    }
                    else
                    {
                        orderService.SellLimit(activeOrder, _tradingState.MarketStreamOnSpot.FirstOrDefault(p => p.s == symbol), message);
                    }
                }

                // TREND SCORE EXIT - Check for strong trend reversal
                var trendScore = TradeHelper.CalculateTrendScore(symbolCandleList, _config.UseWeightedTrendScore);
                if (trendScore <= _config.TrendScoreExitThreshold)
                {
                    LogAndSell($"trend reversal (score: {trendScore})");
                    return;
                }

                // Dynamic stop loss tightening when trend weakens
                if (_config.EnableDynamicStopLoss && trendScore <= 0 && trendScore > _config.TrendScoreExitThreshold)
                {
                    var tightenedStopLoss = lastPrice.Value * (1 - _config.WeakTrendStopLossPercentage / 100);
                    if (tightenedStopLoss > activeOrder.StopLose)
                    {
                        _logger.LogInformation("Tightening stop loss for {Symbol} due to weakening trend (score: {Score}). New stop: {NewStop}",
                            symbol, trendScore, tightenedStopLoss);
                        activeOrder.StopLose = tightenedStopLoss;
                    }
                }

                // Check time and close if necessary
                TimeSpan span = DateTime.Now.Subtract(DateTime.Parse(activeOrder.OpenDate));
                if (activeOrder.HighPrice <= activeOrder.OpenPrice && span.TotalMinutes > _config.TimeBasedKillMinutes)
                {
                    LogAndSell($"time-based kill (stalled for {span.TotalMinutes:F0} min)");
                    return;
                }

                // Check stop loss
                if (lastPrice < activeOrder.StopLose)
                {
                    LogAndSell("stop loss triggered");
                    return;
                }

                // Take profit
                if (lastPrice <= activeOrder.TakeProfit && lastPrice > activeOrder.OpenPrice)
                {
                    LogAndSell($"take profit (price: {lastPrice:F2} <= target: {activeOrder.TakeProfit:F2})");
                    return;
                }

                // AI close - strong bearish prediction
                var mlPrediction = _mlService.MLPredList.FirstOrDefault(p => p.Symbol == activeOrder.Symbol);
                if (mlPrediction != null && mlPrediction.PredictedLabel == "down" && mlPrediction.Score[0] >= 0.97)
                {
                    LogAndSell($"AI exit signal (DOWN with {mlPrediction.Score[0] * 100:F1}% confidence)");
                    return;
                }

                if (_tradingState.IsDbBusy)
                    return;

                // Reload only the required properties
                dbContext.Entry(activeOrder).Reload();
                orderService.UpdateTakeProfit(symbolCandleList, activeOrder, _config.TakeProfitPercentage);
                orderService.UpdateStopLoss(symbolCandleList, activeOrder);
                orderService.SaveHighLow(symbolCandleList, activeOrder);
            }
        }

        private bool IsShort(MarketStream symbolSpot, List<Candle> symbolCandle)
        {
            return true;
        }

        private async Task ReviewPendingOrder(ApplicationDbContext dbContext)
        {
            if (i < 5)
            {
                i++;
                return;
            }
            else
            {
                i = 0;
                CheckSellOrder(dbContext);
                await CheckBuyOrder(dbContext);
                return;
            }
        }

        private void CheckSellOrder(ApplicationDbContext dbContext)
        {
            //1 read on local db pending order buy or sell
            var dbOrderList = dbContext.Order.Where(p => p.Status != "FILLED" && p.Side == "SELL" && p.SellOrderId != 0).Select(p => p).ToList();

            //No order in the pool
            if (!dbOrderList.Any()) return;

            //For each pending order in local db
            foreach (var dbOrder in dbOrderList)
            {
                dbContext.Entry(dbOrder).Reload();   //we reload the entity from the database to get last changes !!!!

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
                    _tradingState.OnHold.Remove(dbOrder.Symbol);
                }
            }

            _hub.Clients.All.SendAsync("refreshUI");
            return;
        }

        private async Task CheckBuyOrder(ApplicationDbContext dbContext)
        {
            //1 read on local db pending order buy or sell
            var dbOrderList = dbContext.Order.Where(p => p.Status != "FILLED" && p.Side == "BUY").Select(p => p).ToList();

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
                        _tradingState.OnHold.Remove(dbOrder.Symbol);
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
            _tradingState.TestBuyLimit = true;
        }
        #endregion
    }
}
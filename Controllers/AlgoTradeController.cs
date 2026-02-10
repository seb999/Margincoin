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
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AlgoTradeController : ControllerBase
    {
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
        private readonly LSTMPredictionService _predictionService;
        private readonly OpenAIPredictionService _openAiService;
        private readonly ICandleDataService _candleDataService;
        private static readonly string PredictionDownLabel = MyEnum.PredictionDirection.Down.ToLabel();

        private List<MarketStream> _marketStreamBuffer = new();
        private List<MarketStream> _marketStreamOnSpot = new();
        private int _marketUpCount = 0;
        private int _pendingOrderCheckCounter = 0;

        private readonly object candleMatrixLock = new();
        private readonly Dictionary<string, int> _lastTrendScores = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lastReplacementBySymbol = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _replacementWindowStart = DateTime.UtcNow;
        private int _replacementWindowCount = 0;
        private bool _replacementInFlight = false;

        #endregion

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
            IServiceScopeFactory serviceScopeFactory,
            LSTMPredictionService predictionService,
            OpenAIPredictionService openAiService,
            ICandleDataService candleDataService)
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
            _predictionService = predictionService;
            _openAiService = openAiService;
            _candleDataService = candleDataService;

            // Configure binance service
            _binanceService.Interval = _config.Interval;
            _binanceService.Limit = _config.MaxCandle;

            // Initialize trading state
            _tradingState.SyncBinanceSymbol = false;

            _tradingState.SymbolWeTrade = _symbolService.GetTopSymbols(_config.NumberOfSymbols);
            _tradingState.SymbolBaseList = _symbolService.GetTopSymbols(_config.NumberOfSymbols);

        }

        #endregion

        #region HTTP API Endpoints

        [HttpGet("[action]")]
        public async Task<string> MonitorMarket()
        {
            _logger.LogWarning("Start trading market with centralized candle collection...");
            _mlService.CleanImageFolder();

            // Step 1: Fetch fresh candle data from Binance and load directly into memory
            await LoadFreshCandleDataIntoMemory();

            // Step 2: Open centralized kline WebSocket for real-time updates
            await OpenCentralizedKlineWebSocket();

            // Step 3: Open Spot WebSocket for market overview
            await OpenWebSocketOnSpot();

            _watchDog.Clear();
            return "Market monitoring started with centralized candle collection";
        }

        [HttpGet("[action]/{id}/{lastPrice}")]
        public async Task CloseTrade(int id, double lastPrice)
        {
            var order = _appDbContext.Order.FirstOrDefault(p => p.Id == id);
            if (order == null) return;

            var orderId = order.Side == "BUY" ? order.BuyOrderId : order.SellOrderId;

            if (order.Status == "NEW")
            {
                _binanceService.CancelOrder(order.Symbol, orderId);
            }
            else
            {
                // Get current AI prediction for manual close
                double exitAiScore = 0;
                string exitAiPrediction = "";
                try
                {
                    var mlPrediction = _mlService?.MLPredList?.FirstOrDefault(p => p.Symbol == order.Symbol);
                    if (mlPrediction != null && mlPrediction.Score != null && mlPrediction.Score.Length > 0)
                    {
                        exitAiScore = mlPrediction.Confidence;
                        exitAiPrediction = mlPrediction.PredictedLabel ?? string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get exit AI prediction for manual close {Symbol}", order.Symbol);
                }

                if (_tradingState.IsMarketOrder == true)
                    await _orderService.SellMarket(order, "by user", exitAiScore, exitAiPrediction);
                else
                    await _orderService.SellLimit(order, _tradingState.AllMarketData.SingleOrDefault(p => p.s == order.Symbol), "by user", exitAiScore, exitAiPrediction);
            }
        }

        [HttpGet("[action]")]
        public void SyncBinanceSymbol()
        {
            var allSymbols = _binanceService.GetSymbolPrice()
                ?.Select(p => p.symbol)
                .Where(s => s.Contains("USDC"))
                .ToList();

            if (allSymbols?.Count > 0)
            {
                _symbolService.SyncBinanceSymbols(allSymbols);
                _symbolService.UpdateCoinMarketCap();
                // Refresh in-memory symbol lists
                _tradingState.SymbolBaseList = _symbolService.GetTopSymbols(_config.NumberOfSymbols);
                _tradingState.SymbolWeTrade = _symbolService.GetTopSymbols(_config.NumberOfSymbols);
            }
        }

        [HttpGet("[action]")]
        public List<BinancePrice> GetSymbolPrice()
        {
            return _binanceService.GetSymbolPrice();
        }

        /// <summary>
        /// Initialize historical candle data for all trading symbols
        /// Fetches last 100 candles from Binance and saves to database
        /// </summary>
        /// <summary>
        /// Load fresh candle data from Binance directly into memory (no database)
        /// </summary>
        private async Task LoadFreshCandleDataIntoMemory()
        {
            _logger.LogWarning("Loading fresh candle data from Binance for {Count} target symbols...", _config.NumberOfSymbols);

            var allSymbols = _tradingState.SymbolBaseList;
            if (allSymbols == null || allSymbols.Count == 0)
            {
                _logger.LogWarning("No symbols found in SymbolBaseList");
                return;
            }

            // Take only the number of symbols configured
            var targetSymbols = allSymbols.Take(_config.NumberOfSymbols).ToList();
            _logger.LogInformation("Selected {Count} symbols out of {Total} available",
                targetSymbols.Count, allSymbols.Count);

            // Clear existing candle matrix
            lock (candleMatrixLock)
            {
                _tradingState.CandleMatrix.Clear();
            }

            int successCount = 0;
            int failCount = 0;

            foreach (var symbol in targetSymbols)
            {
                try
                {
                    // Fetch candles directly from Binance
                    var tempMatrix = new List<List<Candle>>();
                    _binanceService.GetCandles(symbol.SymbolName, ref tempMatrix);

                    if (tempMatrix.Count > 0 && tempMatrix[0].Count >= 50)
                    {
                        var candles = tempMatrix[0];

                        // Calculate indicators
                        TradeIndicator.CalculateIndicator(candles);

                        // Add directly to memory (CandleMatrix)
                        lock (candleMatrixLock)
                        {
                            _tradingState.CandleMatrix.Add(candles);
                        }

                        successCount++;
                        _logger.LogInformation("Loaded {Count} candles for {Symbol} into memory ({Success}/{Total})",
                            candles.Count, symbol.SymbolName, successCount, targetSymbols.Count);
                    }
                    else
                    {
                        failCount++;
                        _logger.LogWarning("Insufficient candles for {Symbol}", symbol.SymbolName);
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    _logger.LogError(ex, "Failed to load candles for {Symbol}", symbol.SymbolName);
                }

                // Small delay to avoid rate limiting
                await Task.Delay(100);
            }

            _logger.LogWarning("Loaded {Success} symbols into CandleMatrix, {Failed} failed",
                successCount, failCount);
        }

        [HttpGet("[action]")]
        public async Task<string> InitializeCandleData()
        {
            _logger.LogWarning("Starting candle data initialization for all symbols...");

            var symbols = _tradingState.SymbolBaseList;
            if (symbols == null || symbols.Count == 0)
            {
                _logger.LogWarning("No symbols found in SymbolBaseList");
                return "No symbols to initialize";
            }

            int successCount = 0;
            int failCount = 0;

            foreach (var symbol in symbols)
            {
                try
                {
                    await LoadHistoricalCandlesForSymbol(symbol.SymbolName);
                    successCount++;
                    _logger.LogInformation("Loaded historical candles for {Symbol} ({Count}/{Total})",
                        symbol.SymbolName, successCount + failCount, symbols.Count);
                }
                catch (Exception ex)
                {
                    failCount++;
                    _logger.LogError(ex, "Failed to load candles for {Symbol}", symbol.SymbolName);
                }

                // Small delay to avoid rate limiting
                await Task.Delay(100);
            }

            var message = $"Candle initialization complete: {successCount} succeeded, {failCount} failed";
            _logger.LogWarning(message);
            return message;
        }

        #endregion

        #region WebSocket Management

        private void UpdateMatrix(StreamData streamData, List<Candle> candleList)
        {
            // Validate inputs
            if (streamData?.k == null || candleList == null) return;

            bool trendScoreChanged = false;
            List<Candle> candleSnapshot = null;
            string symbolName = streamData.k.s;

            lock (candleMatrixLock)
            {
                Candle newCandle = new()
                {
                    s = streamData.k.s,
                    o = streamData.k.o,
                    h = streamData.k.h,
                    l = streamData.k.l,
                    c = streamData.k.c,
                    P = TradeHelper.CalculPourcentChange(streamData.k.c, candleList, _config.Interval, 2),
                };

                // Update or add candle
                if (!streamData.k.x)  // Candle not closed - update last
                {
                    if (candleList.Count == 0) return;
                    candleList[^1] = newCandle;  // Replace in-place
                }
                else  // Candle closed - add new
                {
                    candleList.Add(newCandle);
                    _logger.LogInformation("New candle saved: {Symbol}", streamData.k.s);
                    _tradingState.OnHold.Remove(streamData.k.s);
                }

                // Calculate RSI / MACD / EMA - modifies list in-place
                TradeIndicator.CalculateIndicator(candleList);

                // Calculate the slope of the MACD historic (derivative)
                if (candleList.Count > 0)
                {
                    candleList[^1].MacdSlope =
                        TradeHelper.CalculateMacdSlope(candleList, _config.Interval).Slope;
                }

                // Calculate Trend Score on every tick (very cheap: O(1) comparisons only)
                // This ensures portfolio always shows current TS and system is ready immediately
                var trendScore = TradeHelper.CalculateTrendScore(candleList, _config.UseWeightedTrendScore);

                // Track TS changes to trigger AI predictions only when score actually changes
                if (!_lastTrendScores.TryGetValue(symbolName, out var previousScore) || previousScore != trendScore)
                {
                    _lastTrendScores[symbolName] = trendScore;
                    trendScoreChanged = true;
                    candleSnapshot = candleList.ToList();
                    _logger.LogDebug("TS changed for {Symbol}: {Previous} → {New}", symbolName, previousScore, trendScore);
                }

                // Note: candleList is already a reference to the list in _tradingState.CandleMatrix
                // so modifications are already persisted. No need to replace.

                // Only re-sort when candle closes (not on every tick)
                if (streamData.k.x)
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
                        symbol = streamData.k.s,
                        price = streamData.k.c,
                        change = newCandle.P
                    }));
                }
            }

            if (trendScoreChanged && candleSnapshot?.Count >= 50 && _predictionService != null && _config.EnableMLPredictions)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _predictionService.PredictAsync(symbolName, candleSnapshot);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "AI prediction trigger failed for {Symbol}", symbolName);
                    }
                });
            }
        }

        public async Task OpenWebSocketOnSpot()
        {
            // Close existing WebSocket if it exists
            if (_webSocket.ws1 != null)
            {
                _logger.LogWarning("Closing existing Spot WebSocket before reopening");
                try
                {
                    await _webSocket.ws1.DisconnectAsync(CancellationToken.None);
                    _webSocket.ws1 = null;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing existing Spot WebSocket");
                    _webSocket.ws1 = null;
                }
            }

            _logger.LogWarning("Creating Spot WebSocket with stream: {Stream}", _config.SpotTickerTime);
            _webSocket.ws1 = new MarketDataWebSocket(_config.SpotTickerTime);
            var dataResult = new StringBuilder();

            _webSocket.ws1.OnMessageReceived(
                 (data) =>
                 {
                     dataResult.Append(data);
                     var fullData = dataResult.ToString();

                     if (fullData.Contains("}]"))
                     {
                         int bracketIndex = fullData.IndexOf(']');
                         if (fullData.Length > (bracketIndex + 1))
                         {
                             fullData = fullData[..(bracketIndex + 1)];
                         }

                         List<MarketStream> marketStreamList = Helper.deserializeHelper<List<MarketStream>>(fullData);
                         dataResult.Clear();  //we clean it immediately to avoid a bug on new data coming

                        marketStreamList = marketStreamList.Where(p => p.s.Contains("USDC")).ToList();

                         TradeHelper.BufferMarketStream(marketStreamList, ref _marketStreamBuffer);

                        _marketUpCount = _marketStreamBuffer.Count(pred => pred.P >= 0);

                         // Store all market data for later use with active orders
                         _tradingState.AllMarketData = [.. _marketStreamBuffer];

                         _marketStreamOnSpot = _marketStreamBuffer.Where(p => _tradingState.SymbolBaseList.Any(p1 => p1.SymbolName == p.s))
                             .OrderByDescending(p => p.P)
                             .ToList();

                         _tradingState.MarketStreamOnSpot = _marketStreamOnSpot;

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
                    message = "Market data streaming active",
                    interval = _config.Interval
                }));

                // Keep connection alive - do NOT disconnect
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open Spot WebSocket");
                _webSocket.ws1 = null;
            }
        }

        /// <summary>
        /// Load CandleMatrix from database for all symbols that have enough candles
        /// </summary>
        private async Task LoadCandleMatrixFromDatabase()
        {
            _logger.LogWarning("Loading candle matrix from database...");

            var symbolsReady = await _candleDataService.GetSymbolsReadyForTradingAsync(_config.Interval, 50);

            // Limit to NumberOfSymbols from configuration
            var symbolsToLoad = symbolsReady.Take(_config.NumberOfSymbols).ToList();

            lock (candleMatrixLock)
            {
                _tradingState.CandleMatrix.Clear();
            }

            foreach (var symbolName in symbolsToLoad)
            {
                try
                {
                    int maxCandle = int.Parse(_config.MaxCandle);
                    var candles = await _candleDataService.GetCandlesAsync(symbolName, _config.Interval, maxCandle);

                    if (candles.Count >= 50)
                    {
                        // Calculate indicators for the candles
                        TradeIndicator.CalculateIndicator(candles);

                        lock (candleMatrixLock)
                        {
                            _tradingState.CandleMatrix.Add(candles);
                        }

                        _logger.LogDebug("Loaded {Count} candles for {Symbol}", candles.Count, symbolName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load candles from database for {Symbol}", symbolName);
                }
            }

            _logger.LogWarning("Loaded candle matrix for {Count} symbols", _tradingState.CandleMatrix.Count);
        }

        /// <summary>
        /// Open centralized WebSocket(s) that subscribe to kline streams for all trading symbols
        /// Creates multiple WebSockets if more than 50 symbols (Binance limit per connection)
        /// </summary>
        private async Task OpenCentralizedKlineWebSocket()
        {
            _logger.LogWarning("Opening centralized kline WebSocket(s) for all symbols...");

            // Close existing WebSockets if they exist (important when number of symbols changes)
            if (_webSocket.KlineWebSockets.Count > 0)
            {
                _logger.LogWarning("Closing {Count} existing kline WebSocket(s) before reopening", _webSocket.KlineWebSockets.Count);
                foreach (var existingWs in _webSocket.KlineWebSockets.ToList())
                {
                    try
                    {
                        await existingWs.DisconnectAsync(CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error closing existing kline WebSocket");
                    }
                }
                _webSocket.KlineWebSockets.Clear();
            }

            // Get symbols from in-memory CandleMatrix (not from database)
            List<string> symbolsReady;
            lock (candleMatrixLock)
            {
                symbolsReady = _tradingState.CandleMatrix
                    .Where(c => c.Count > 0)
                    .Select(c => c[0].s)
                    .ToList();
            }

            if (symbolsReady.Count == 0)
            {
                _logger.LogWarning("No symbols in CandleMatrix. Skipping WebSocket setup.");
                return;
            }

            _logger.LogInformation("Opening WebSocket(s) for {Count} symbols from CandleMatrix", symbolsReady.Count);

            // Split symbols into chunks of 50 (Binance limit per WebSocket)
            const int maxStreamsPerWebSocket = 50;
            var symbolChunks = symbolsReady
                .Select((symbol, index) => new { symbol, index })
                .GroupBy(x => x.index / maxStreamsPerWebSocket)
                .Select(group => group.Select(x => x.symbol).ToList())
                .ToList();

            _logger.LogInformation("Creating {Count} WebSocket(s) for {Total} symbols", symbolChunks.Count, symbolsReady.Count);

            // Create a WebSocket for each chunk
            for (int i = 0; i < symbolChunks.Count; i++)
            {
                var chunk = symbolChunks[i];
                var streams = chunk.Select(s => $"{s.ToLower()}@kline_{_config.Interval}").ToList();
                var combinedStream = string.Join("/", streams);
                var wsIndex = i + 1;

                _logger.LogInformation("WebSocket #{Index}: Subscribing to {Count} kline streams", wsIndex, streams.Count);

                var ws = new MarketDataWebSocket(combinedStream);

                ws.OnMessageReceived(
                    (data) =>
                    {
                        try
                        {
                            // Binance sometimes appends null bytes to JSON frames; strip them before parsing
                            if (data?.IndexOf('\0') >= 0)
                            {
                                data = data.Replace("\0", string.Empty);
                            }

                            // Parse the incoming kline data
                            var streamData = Helper.deserializeHelper<StreamData>(data);

                            if (streamData?.k == null) return Task.CompletedTask;

                            // ALWAYS update in-memory matrix immediately (fast, no DB locks)
                            lock (candleMatrixLock)
                            {
                                var symbolCandles = _tradingState.CandleMatrix
                                    .FirstOrDefault(c => c.Count > 0 && c[0].s == streamData.k.s);

                                if (symbolCandles != null)
                                {
                                    UpdateMatrix(streamData, symbolCandles);
                                }
                                else
                                {
                                    _logger.LogDebug("Received candle for {Symbol} but not in CandleMatrix", streamData.k.s);
                                }
                            }

                            // ONLY save to database when candle CLOSES (reduces DB writes by 300x!)
                            if (streamData.k.x)  // Candle is closed
                            {
                                // Fire-and-forget: don't await to avoid blocking WebSocket
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await _candleDataService.SaveCandleAsync(streamData.k.s, _config.Interval, streamData);
                                        _logger.LogDebug("Saved closed candle for {Symbol}", streamData.k.s);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Failed to save closed candle for {Symbol}", streamData.k.s);
                                    }
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing kline WebSocket message");
                        }

                        return Task.CompletedTask;
                    }, CancellationToken.None);

                try
                {
                    await ws.ConnectAsync(CancellationToken.None);
                    _webSocket.KlineWebSockets.Add(ws);
                    _logger.LogWarning("WebSocket #{Index} connected for {Count} symbols", wsIndex, streams.Count);

                    await _hub.Clients.All.SendAsync("websocketStatus", JsonSerializer.Serialize(new
                    {
                        symbol = $"KLINE_WS_{wsIndex}",
                        status = "connected",
                        message = $"Streaming {streams.Count} symbols",
                        interval = _config.Interval
                    }));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to open kline WebSocket #{Index}", wsIndex);
                }
            }

            _logger.LogWarning("All {Count} kline WebSocket(s) setup complete for {Total} symbols",
                _webSocket.KlineWebSockets.Count, symbolsReady.Count);
        }

        /// <summary>
        /// Load historical candles for a symbol and save to database
        /// </summary>
        private async Task LoadHistoricalCandlesForSymbol(string symbolName)
        {
            try
            {
                // Check if we already have enough candles
                var hasEnough = await _candleDataService.HasEnoughCandlesAsync(symbolName, _config.Interval, 50);
                if (hasEnough)
                {
                    _logger.LogDebug("Symbol {Symbol} already has sufficient candles, skipping", symbolName);
                    return;
                }

                // Fetch candles from Binance API (gets 100 candles)
                var tempMatrix = new List<List<Candle>>();
                _binanceService.GetCandles(symbolName, ref tempMatrix);

                if (tempMatrix.Count == 0 || tempMatrix[0].Count == 0)
                {
                    _logger.LogWarning("No candles returned from Binance for {Symbol}", symbolName);
                    return;
                }

                var candles = tempMatrix[0];

                // Convert and save each candle to database
                using var scope = _serviceScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                foreach (var candle in candles)
                {
                    var candleHistory = new CandleHistory
                    {
                        Symbol = symbolName,
                        Interval = _config.Interval,
                        OpenTime = (long)candle.T,
                        CloseTime = (long)candle.t,
                        Open = candle.o,
                        High = candle.h,
                        Low = candle.l,
                        Close = candle.c,
                        Volume = candle.v,
                        IsClosed = true, // Historical candles are always closed
                        PriceChangePercent = candle.P,
                        UpdatedAt = DateTime.UtcNow
                    };

                    // Check if candle already exists
                    var existing = await dbContext.CandleHistory
                        .FirstOrDefaultAsync(c =>
                            c.Symbol == symbolName &&
                            c.Interval == _config.Interval &&
                            c.OpenTime == (long)candle.T);

                    if (existing == null)
                    {
                        dbContext.CandleHistory.Add(candleHistory);
                    }
                }

                await dbContext.SaveChangesAsync();
                _logger.LogInformation("Saved {Count} historical candles for {Symbol}", candles.Count, symbolName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load historical candles for {Symbol} - symbol may not be available on this Binance environment", symbolName);
                // Don't throw - continue with other symbols
            }
        }

        #endregion

        #region Trading Algorithm
        private async Task ProcessMarketMatrix()
        {
            // Create a new scope for database operations called from WebSocket callbacks
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var settingsService = scope.ServiceProvider.GetRequiredService<ITradingSettingsService>();

                try
                {
                    // If CandleMatrix is empty, try to populate it once to avoid a no-op loop
                    if (_tradingState.CandleMatrix.Count == 0)
                    {
                        _logger.LogWarning("CandleMatrix is empty when processing market; reloading from database");
                        await LoadCandleMatrixFromDatabase();
                        _logger.LogWarning("CandleMatrix reload complete. Count={Count}", _tradingState.CandleMatrix.Count);
                        if (_tradingState.CandleMatrix.Count == 0)
                        {
                            _logger.LogWarning("CandleMatrix still empty after reload; skipping ProcessMarketMatrix iteration");
                            return;
                        }
                    }

                    // Get runtime settings from the scoped service (do this early to use throughout)
                    var runtime = await settingsService.GetRuntimeSettingsAsync();

                    await ReviewPendingOrder(orderService, dbContext);

                    // Load current active orders once for guard logic
                    var activeOrders = orderService.GetActiveOrder();

                    // Ensure OnHold contains all currently active symbols to avoid duplicate entries
                    foreach (var order in activeOrders)
                    {
                        if (!_tradingState.OnHold.ContainsKey(order.Symbol))
                        {
                            _tradingState.OnHold[order.Symbol] = true;
                        }
                    }

                    //Review open orders
                    foreach (var item in activeOrders)
                    {
                        await ReviewOpenTrade(item.Symbol, orderService, dbContext, runtime);
                    }

                    // Refresh active orders list in case any were closed in ReviewOpenTrade
                    activeOrders = orderService.GetActiveOrder();

                    // Periodic memory cleanup - clean stale OnHold entries and old data
                    var activeSymbols = activeOrders.Select(o => o.Symbol).ToHashSet();
                    _tradingState.CleanupStaleOnHold(activeSymbols);
                    _tradingState.CleanupOldData(); // Only runs every 15 minutes internally
                    _mlService?.UpdateML(); // Cleanup expired ML prediction cache

                    if (activeOrders.Count < runtime.MaxOpenTrades)
                    {
                        foreach (var symbolCandelList in _tradingState.CandleMatrix.Take(runtime.MaxCandidateDepth).ToList())
                        {
                            await ReviewSpotMarket(symbolCandelList, orderService, runtime);
                        }
                    }
                    else if (runtime.EnableAggressiveReplacement)
                    {
                        await TryAggressiveReplacement(_tradingState.AllMarketData, orderService, activeOrders, runtime);
                    }

                    // Send market data for active orders to update UI
                    var activeOrderSymbols = activeOrders.Select(o => o.Symbol).ToList();
                    var marketDataForUI = _tradingState.AllMarketData?
                        .Where(m => activeOrderSymbols.Contains(m.s))
                        .ToList() ?? [];

                    await _hub.Clients.All.SendAsync("trading", JsonSerializer.Serialize(marketDataForUI));
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

        private async Task ReviewSpotMarket(List<Candle> symbolCandleList, IOrderService orderService, RuntimeTradingSettings runtime)
        {
            var marketData = _tradingState.AllMarketData.FirstOrDefault(p => p.s == symbolCandleList.Last().s);

            if (marketData == null)
            {
                return;
            }

            var activeOrders = orderService.GetActiveOrder();
            var activeOrder = activeOrders.FirstOrDefault(p => p.Symbol == marketData.s);
            var activeOrderCount = activeOrders.Count;

            if (activeOrder == null && activeOrderCount < runtime.MaxOpenTrades)
            {
                if (await EnterLongPosition(marketData, symbolCandleList, runtime))
                {
                    _tradingState.OnHold.TryAdd(marketData.s, true);

                    Console.WriteLine($"Opening trade on {marketData.s}");

                    // Get AI prediction safely - defaults to empty if not available
                    double aiScore = 0;
                    string aiPrediction = "";
                    try
                    {
                        var mlPrediction = _mlService?.MLPredList?.FirstOrDefault(p => p.Symbol == marketData.s);
                        if (mlPrediction != null && mlPrediction.Score != null && mlPrediction.Score.Length > 0)
                        {
                            aiScore = mlPrediction.Confidence;
                            aiPrediction = mlPrediction.PredictedLabel?.ToLower() ?? "";
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get AI prediction for {Symbol}, continuing with order", marketData.s);
                    }

                    if (_tradingState.IsMarketOrder == true)
                    {
                        await orderService.BuyMarket(marketData, symbolCandleList, aiScore, aiPrediction);
                    }
                    else
                    {
                        await orderService.BuyLimit(marketData, symbolCandleList, aiScore, aiPrediction);
                    }
                }

                if (_tradingState.TestBuyLimit == true)
                {
                    _tradingState.TestBuyLimit = false;
                    Console.WriteLine($"Opening test trade on {marketData.s}");

                    // Get AI prediction safely for test buy
                    double aiScore = 0;
                    string aiPrediction = "";
                    try
                    {
                        var mlPrediction = _mlService?.MLPredList?.FirstOrDefault(p => p.Symbol == marketData.s);
                        if (mlPrediction != null && mlPrediction.Score != null && mlPrediction.Score.Length > 0)
                        {
                            aiScore = mlPrediction.Confidence;
                            aiPrediction = mlPrediction.PredictedLabel?.ToLower() ?? "";
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get AI prediction for {Symbol}, continuing with order", marketData.s);
                    }

                    await orderService.BuyLimit(marketData, symbolCandleList, aiScore, aiPrediction);
                }
            }
        }

        private async Task TryAggressiveReplacement(List<MarketStream> marketStreamList, IOrderService orderService, List<Order> activeOrders, RuntimeTradingSettings runtime)
        {
            if (_replacementInFlight || marketStreamList == null || marketStreamList.Count == 0)
            {
                return;
            }

            var bestCandidate = GetBestSurgeCandidate(marketStreamList, activeOrders, runtime);
            if (bestCandidate == null)
            {
                return;
            }

            var weakestOpenOrder = GetWeakestOpenOrder(marketStreamList, activeOrders);
            if (weakestOpenOrder == null)
            {
                return;
            }

            if (IsReplacementRateLimited(bestCandidate.Value.Symbol, weakestOpenOrder.Value.Order.Symbol, runtime))
            {
                _logger.LogDebug("Replacement skipped due to rate limits or cooldown. New={NewSymbol}, Old={OldSymbol}", bestCandidate.Value.Symbol, weakestOpenOrder.Value.Order.Symbol);
                return;
            }

            if (bestCandidate.Value.Score <= weakestOpenOrder.Value.KeepScore * (1 + runtime.ReplacementScoreGap))
            {
                _logger.LogDebug("Replacement skipped: surge score {NewScore:F2} not strong enough vs keep score {KeepScore:F2}", bestCandidate.Value.Score, weakestOpenOrder.Value.KeepScore);
                return;
            }

            _replacementInFlight = true;
            try
            {
                _logger.LogWarning("Aggressive replacement: closing {OldSymbol} (keepScore {KeepScore:F2}) for {NewSymbol} (surgeScore {SurgeScore:F2})",
                    weakestOpenOrder.Value.Order.Symbol, weakestOpenOrder.Value.KeepScore, bestCandidate.Value.Symbol, bestCandidate.Value.Score);

                var exitAi = GetAiPrediction(weakestOpenOrder.Value.Order.Symbol);

                if (_tradingState.IsMarketOrder || weakestOpenOrder.Value.MarketData == null)
                {
                    await orderService.SellMarket(weakestOpenOrder.Value.Order, "replacement", exitAi.Score, exitAi.Prediction);
                }
                else
                {
                    await orderService.SellLimit(weakestOpenOrder.Value.Order, weakestOpenOrder.Value.MarketData, "replacement", exitAi.Score, exitAi.Prediction);
                }

                if (!_tradingState.OnHold.ContainsKey(bestCandidate.Value.Symbol))
                {
                    _tradingState.OnHold[bestCandidate.Value.Symbol] = true;
                }

                if (_tradingState.IsMarketOrder)
                {
                    await orderService.BuyMarket(bestCandidate.Value.MarketData, bestCandidate.Value.Candles, bestCandidate.Value.AiScore, bestCandidate.Value.AiPrediction);
                }
                else
                {
                    await orderService.BuyLimit(bestCandidate.Value.MarketData, bestCandidate.Value.Candles, bestCandidate.Value.AiScore, bestCandidate.Value.AiPrediction);
                }

                MarkReplacementExecuted(bestCandidate.Value.Symbol, weakestOpenOrder.Value.Order.Symbol);

                await _hub.Clients.All.SendAsync("replacement", JsonSerializer.Serialize(new
                {
                    replaced = weakestOpenOrder.Value.Order.Symbol,
                    added = bestCandidate.Value.Symbol,
                    newScore = bestCandidate.Value.Score,
                    replacedScore = weakestOpenOrder.Value.KeepScore
                }));
            }
            finally
            {
                _replacementInFlight = false;
            }
        }

        private (string Symbol, MarketStream MarketData, List<Candle> Candles, double Score, double AiScore, string AiPrediction)? GetBestSurgeCandidate(List<MarketStream> marketStreamList, List<Order> activeOrders, RuntimeTradingSettings runtime)
        {
            List<(string Symbol, MarketStream MarketData, List<Candle> Candles, double Score, double AiScore, string AiPrediction)> candidates = new();

            lock (candleMatrixLock)
            {
                foreach (var candles in _tradingState.CandleMatrix.Take(runtime.MaxCandidateDepth))
                {
                    if (candles == null || candles.Count < 3)
                        continue;

                    var symbol = candles[^1].s;
                    if (activeOrders.Any(o => o.Symbol == symbol))
                        continue;

                    if (_tradingState.OnHold.TryGetValue(symbol, out var isOnHold) && isOnHold)
                        continue;

                    var marketData = marketStreamList.FirstOrDefault(m => m.s == symbol);
                    if (marketData == null)
                        continue;

                    var score = CalculateSurgeScore(marketData, candles);
                    if (double.IsNegativeInfinity(score) || score < runtime.SurgeScoreThreshold)
                        continue;

                    var ai = GetAiPrediction(symbol);
                    candidates.Add((symbol, marketData, candles.ToList(), score, ai.Score, ai.Prediction));
                }
            }

            if (candidates.Count == 0)
                return null;

            return candidates.OrderByDescending(c => c.Score).First();
        }

        private (Order Order, double KeepScore, MarketStream MarketData, List<Candle> Candles)? GetWeakestOpenOrder(List<MarketStream> marketStreamList, List<Order> activeOrders)
        {
            (Order Order, double KeepScore, MarketStream MarketData, List<Candle> Candles)? weakest = null;

            lock (candleMatrixLock)
            {
                foreach (var order in activeOrders)
                {
                    var candles = _tradingState.CandleMatrix.FirstOrDefault(c => c.Count > 0 && c.Last().s == order.Symbol);
                    if (candles == null || candles.Count == 0)
                        continue;

                    var keepScore = CalculateKeepScore(order, candles);
                    var marketData = marketStreamList.FirstOrDefault(m => m.s == order.Symbol);

                    if (weakest == null || keepScore < weakest.Value.KeepScore)
                    {
                        weakest = (order, keepScore, marketData, candles.ToList());
                    }
                }
            }

            return weakest;
        }

        private double CalculateSurgeScore(MarketStream marketData, List<Candle> symbolCandles)
        {
            if (symbolCandles == null || symbolCandles.Count < 3)
                return double.NegativeInfinity;

            var last = symbolCandles[^1];
            var prev = symbolCandles[^2];
            var prev2 = symbolCandles[^3];

            // Keep RSI within configured bounds to avoid overbought/oversold churn
            if (last.Rsi < _config.MinRSI || last.Rsi > _config.MaxRSI)
                return double.NegativeInfinity;

            var trendScore = TradeHelper.CalculateTrendScore(symbolCandles, _config.UseWeightedTrendScore);
            var recentMove = prev.c > 0 ? (last.c - prev.c) / prev.c * 100 : 0;
            var prevMove = prev2.c > 0 ? (prev.c - prev2.c) / prev2.c * 100 : 0;
            var acceleration = recentMove - prevMove;
            var macdSlopeBoost = Math.Max(0, last.MacdSlope * 100);
            var volumeSpike = prev.v > 0 ? Math.Min(last.v / prev.v, 5) : 1;

            // Weighted composite; tuned to highlight fast upside moves with confirmation
            var score = trendScore * 0.6
                        + recentMove * 0.5
                        + acceleration * 0.3
                        + macdSlopeBoost
                        + (volumeSpike - 1)
                        + marketData.P / 2.0;

            return score;
        }

        private double CalculateKeepScore(Order order, List<Candle> symbolCandles)
        {
            if (order == null || symbolCandles == null || symbolCandles.Count == 0)
                return double.NegativeInfinity;

            var trendScore = TradeHelper.CalculateTrendScore(symbolCandles, _config.UseWeightedTrendScore);
            var lastPrice = symbolCandles.Last().c;
            var stopDistance = order.StopLose > 0 ? (lastPrice - order.StopLose) / lastPrice * 100 : 0;
            var pnl = order.OpenPrice > 0 ? (lastPrice - order.OpenPrice) / order.OpenPrice * 100 : 0;

            double timePenalty = 0;
            if (DateTime.TryParseExact(order.OpenDate, "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var openDt))
            {
                var minutesInTrade = DateTime.Now.Subtract(openDt).TotalMinutes;
                timePenalty = Math.Min(minutesInTrade / 30, 2); // degrade longer holds up to 2 points
            }

            return trendScore + stopDistance + pnl - timePenalty;
        }

        private bool IsReplacementRateLimited(string newSymbol, string oldSymbol, RuntimeTradingSettings runtime)
        {
            var now = DateTime.UtcNow;

            if (_lastReplacementBySymbol.TryGetValue(newSymbol, out var lastNew) &&
                (now - lastNew).TotalSeconds < runtime.ReplacementCooldownSeconds)
                return true;

            if (_lastReplacementBySymbol.TryGetValue(oldSymbol, out var lastOld) &&
                (now - lastOld).TotalSeconds < runtime.ReplacementCooldownSeconds)
                return true;

            if ((now - _replacementWindowStart).TotalHours >= 1)
            {
                _replacementWindowStart = now;
                _replacementWindowCount = 0;
            }

            return _replacementWindowCount >= runtime.MaxReplacementsPerHour;
        }

        private void MarkReplacementExecuted(string newSymbol, string oldSymbol)
        {
            var now = DateTime.UtcNow;
            _lastReplacementBySymbol[newSymbol] = now;
            _lastReplacementBySymbol[oldSymbol] = now;
            _replacementWindowCount++;
        }

        private (double Score, string Prediction) GetAiPrediction(string symbol)
        {
            double aiScore = 0;
            string aiPrediction = "";

            try
            {
                var mlPrediction = _mlService?.MLPredList?.FirstOrDefault(p => p.Symbol == symbol);
                if (mlPrediction != null && mlPrediction.Score != null && mlPrediction.Score.Length > 0)
                {
                    aiScore = mlPrediction.Confidence;
                    aiPrediction = mlPrediction.PredictedLabel?.ToLower() ?? "";
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to fetch AI prediction for {Symbol}", symbol);
            }

            return (aiScore, aiPrediction);
        }

        private async Task<bool> EnterLongPosition(MarketStream marketData, List<Candle> symbolCandles, RuntimeTradingSettings runtime)
        {
            // Validate trend score meets minimum threshold
            var entryTrendScore = TradeHelper.CalculateTrendScore(symbolCandles, _config.UseWeightedTrendScore);

            if (entryTrendScore < _config.MinTrendScoreForEntry)
            {
                _logger.LogDebug("Entry rejected for {Symbol}: Trend score {Score} below threshold {Threshold}", marketData.s, entryTrendScore, _config.MinTrendScoreForEntry);
                return false;
            }

            // Market breadth check (with crash override)
            if (_marketUpCount < _config.MinConsecutiveUpSymbols && marketData.P > _config.MaxSpreadOverride)
            {
                _logger.LogDebug("Entry rejected for {Symbol}: Market breadth too low ({Up} symbols up, need {Min})",
                    marketData.s, _marketUpCount, _config.MinConsecutiveUpSymbols);
                return false;
            }

            // TREND DIRECTION - Primary filter
            var trendDirection = TradeHelper.GetTrendDirection(symbolCandles, _config.UseWeightedTrendScore);
            if (trendDirection != MyEnum.TrendDirection.Up)
            {
                _logger.LogDebug("Entry rejected for {Symbol}: Trend direction is {Direction}, need Up",
                    marketData.s, trendDirection);
                return false;
            }

            // Check if there are enough candles to perform movement validation
            if (symbolCandles.Count > _config.PrevCandleCount)
            {
                // Strong movement validation - check percentage increase
                var percentChange = TradeHelper.CalculPourcentChange(symbolCandles, _config.PrevCandleCount);
                if (percentChange < _config.MinPercentageUp)
                {
                    _logger.LogDebug("Entry rejected for {Symbol}: Movement too weak ({Change}%, need {Min}%)",
                        marketData.s, percentChange, _config.MinPercentageUp);
                    return false;
                }
            }

            // OpenAI Enhanced Signal Check (runtime configurable)
            if (runtime.EnableOpenAISignals)
            {
                try
                {
                    var currentCandle = symbolCandles.LastOrDefault();
                    var previousCandle = symbolCandles.Count > 1 ? symbolCandles[^2] : null;

                    if (currentCandle != null)
                    {
                        var aiAnalysis = await _openAiService.GetTradingSignalAsync(
                            marketData.s,
                            currentCandle,
                            previousCandle);

                        if (aiAnalysis != null)
                        {
                            _logger.LogInformation(
                                "OpenAI Analysis for {Symbol}: {Signal} (Score: {Score}/10, Confidence: {Confidence:P}, Risk: {Risk})",
                                marketData.s, aiAnalysis.Signal, aiAnalysis.TradingScore, aiAnalysis.Confidence, aiAnalysis.RiskLevel);

                            // Check if OpenAI signal meets our criteria
                            if (!OpenAIPredictionService.IsStrongSignal(aiAnalysis,
                                minConfidence: _config.MinOpenAIConfidence,
                                minScore: _config.MinOpenAIScore))
                            {
                                _logger.LogDebug(
                                    "Entry rejected for {Symbol}: OpenAI signal not strong enough. " +
                                    "Signal={Signal}, Score={Score}, Confidence={Confidence:P}, Need: Score>={MinScore}, Confidence>={MinConf:P}",
                                    marketData.s, aiAnalysis.Signal, aiAnalysis.TradingScore,
                                    aiAnalysis.Confidence, _config.MinOpenAIScore, _config.MinOpenAIConfidence);
                                return false;
                            }

                            _logger.LogInformation(
                                "OpenAI APPROVED for {Symbol}. Reasoning: {Reasoning}",
                                marketData.s, aiAnalysis.Reasoning);
                        }
                        else
                        {
                            _logger.LogWarning("OpenAI analysis returned null for {Symbol}, continuing without AI filter", marketData.s);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting OpenAI analysis for {Symbol}, continuing without AI filter", marketData.s);
                }
            }

            // Check the symbol is not on hold
            if (_tradingState.OnHold.TryGetValue(marketData.s, out var isOnHold) && isOnHold)
            {
                _logger.LogDebug("Entry rejected for {Symbol}: Symbol on hold", marketData.s);
                return false;
            }

            // RSI boundaries check (avoid extreme overbought/oversold)
            var currentRsi = symbolCandles.Last().Rsi;
            if (currentRsi < _config.MinRSI || currentRsi > _config.MaxRSI)
            {
                _logger.LogDebug("Entry rejected for {Symbol}: RSI out of range ({RSI}, range: {Min}-{Max})",
                    marketData.s, currentRsi, _config.MinRSI, _config.MaxRSI);
                return false;
            }

            // All checks passed
            _logger.LogInformation("✓ Entry signal for {Symbol}: Trend={Trend}, RSI={RSI}, Change={Change}%",
                marketData.s, trendDirection, currentRsi, marketData.P);
            return true;
        }

        private async Task ReviewOpenTrade(string symbol, IOrderService orderService, ApplicationDbContext dbContext, RuntimeTradingSettings runtime)
        {
            Order activeOrder;
            List<Candle> symbolCandleList;
            Candle currentCandle;
            double? lastPrice;
            double? highPrice;
            string sellReason = null;

            // Lock only for reading shared state
            lock (candleMatrixLock)
            {
                activeOrder = orderService.GetActiveOrder().FirstOrDefault(p => p.Symbol == symbol);
                symbolCandleList = _tradingState.CandleMatrix.FirstOrDefault(p => p.Last().s == symbol);
                currentCandle = symbolCandleList?.Last();
                lastPrice = currentCandle?.c;
                highPrice = currentCandle?.h;

                if (activeOrder == null || currentCandle == null || symbolCandleList == null)
                    return;

                // TREND SCORE EXIT - Check for strong trend reversal
                var trendScore = TradeHelper.CalculateTrendScore(symbolCandleList, _config.UseWeightedTrendScore);
                if (trendScore <= _config.TrendScoreExitThreshold)
                {
                    sellReason = $"trend reversal (score: {trendScore})";
                }
                // Dynamic stop loss tightening when trend weakens
                // Only applies to trades that haven't reached meaningful profit yet (< 1% gain)
                // For profitable trades, the trailing stop loss handles protection
                else if (runtime.EnableDynamicStopLoss &&
                         trendScore <= 0 &&
                         trendScore > _config.TrendScoreExitThreshold &&
                         lastPrice.Value < activeOrder.OpenPrice * 1.01)
                {
                    var tightenedStopLoss = lastPrice.Value * (1 - runtime.WeakTrendStopLossPercentage / 100);
                    if (tightenedStopLoss > activeOrder.StopLose)
                    {
                        var oldStop = activeOrder.StopLose;
                        var currentProfitPct = ((lastPrice.Value - activeOrder.OpenPrice) / activeOrder.OpenPrice) * 100;
                        activeOrder.StopLose = tightenedStopLoss;

                        _logger.LogInformation(
                            "Weak trend stop loss tightened for {Symbol} - OrderId: {OrderId}. " +
                            "Trend score: {Score:F2} (threshold: {Threshold:F2}) | " +
                            "Entry: {Entry:F2}, Current: {Price:F2} ({ProfitPct:F2}%) | " +
                            "Stop: {OldStop:F2} → {NewStop:F2} (tightening {WeakPct}% from current price)",
                            symbol, activeOrder.Id,
                            trendScore, _config.TrendScoreExitThreshold,
                            activeOrder.OpenPrice, lastPrice.Value, currentProfitPct,
                            oldStop, tightenedStopLoss, runtime.WeakTrendStopLossPercentage);
                    }
                }

                // Check time and close if necessary
                if (sellReason == null)
                {
                    TimeSpan span = DateTime.Now.Subtract(DateTime.ParseExact(activeOrder.OpenDate, "dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                    if (activeOrder.HighPrice <= activeOrder.OpenPrice && span.TotalMinutes > runtime.TimeBasedKillMinutes)
                    {
                        sellReason = $"time-based kill (stalled for {span.TotalMinutes:F0} min)";
                    }
                }

                // Check stop loss
                if (sellReason == null && lastPrice < activeOrder.StopLose)
                {
                    sellReason = "stop loss triggered";
                }

                // Take profit
                if (sellReason == null && lastPrice <= activeOrder.TakeProfit && lastPrice > activeOrder.OpenPrice)
                {
                    sellReason = $"take profit (price: {lastPrice:F2} <= target: {activeOrder.TakeProfit:F2})";
                }

                // AI close - strong bearish prediction
                if (sellReason == null)
                {
                    var mlPrediction = _mlService.MLPredList.FirstOrDefault(p => p.Symbol == activeOrder.Symbol);
                if (mlPrediction != null && mlPrediction.PredictedLabel == PredictionDownLabel && mlPrediction.Score[0] >= 0.97)
                {
                    sellReason = $"AI exit signal (DOWN with {mlPrediction.Score[0] * 100:F1}% confidence)";
                }
                }

                // Update order data if not selling
                if (sellReason == null && !_tradingState.IsDbBusy)
                {
                    // Reload only the required properties
                    dbContext.Entry(activeOrder).Reload();
                    // Use atomic update to prevent race conditions between HighPrice and TakeProfit
                    orderService.UpdateOrderPriceTracking(symbolCandleList, activeOrder, runtime.TakeProfitPercentage);
                    orderService.UpdateStopLoss(symbolCandleList, activeOrder);
                }
            }

            // Execute sell outside of lock (async operation)
            if (sellReason != null)
            {
                _logger.LogInformation("Closing trade for {Symbol}: {Reason}", symbol, sellReason);

                // Get current AI prediction at exit
                double exitAiScore = 0;
                string exitAiPrediction = "";
                try
                {
                    var mlPrediction = _mlService?.MLPredList?.FirstOrDefault(p => p.Symbol == symbol);
                    if (mlPrediction != null && mlPrediction.Score != null && mlPrediction.Score.Length > 0)
                    {
                        exitAiScore = mlPrediction.Confidence;
                        exitAiPrediction = mlPrediction.PredictedLabel ?? string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get exit AI prediction for {Symbol}", symbol);
                }

                if (_tradingState.IsMarketOrder)
                {
                    await orderService.SellMarket(activeOrder, sellReason, exitAiScore, exitAiPrediction);
                }
                else
                {
                    await orderService.SellLimit(activeOrder, _tradingState.AllMarketData.FirstOrDefault(p => p.s == symbol), sellReason, exitAiScore, exitAiPrediction);
                }
            }
        }

        private async Task ReviewPendingOrder(IOrderService orderService, ApplicationDbContext dbContext)
        {
            // Check pending orders every 5 cycles to reduce API calls
            if (_pendingOrderCheckCounter < 5)
            {
                _pendingOrderCheckCounter++;
                return;
            }

            _pendingOrderCheckCounter = 0;
            CheckSellOrder(orderService, dbContext);
            await CheckBuyOrder(orderService, dbContext);
        }

        private void CheckSellOrder(IOrderService orderService, ApplicationDbContext dbContext)
        {
            // Get pending sell orders from database
            var pendingSellOrders = dbContext.Order
                .Where(p => p.Status != "FILLED" && p.Side == "SELL" && p.SellOrderId != 0)
                .ToList();

            if (pendingSellOrders.Count == 0) return;

            // Check status of each pending sell order
            foreach (var order in pendingSellOrders)
            {
                dbContext.Entry(order).Reload();   // Reload to get latest changes

                var binanceOrder = _binanceService.OrderStatus(order.Symbol, order.SellOrderId);
                var storedDate = DateTime.ParseExact(order.OrderDate, "dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                var expirationTime = DateTime.Now.AddSeconds(-50);
                var orderStatus = binanceOrder.status;

                // Cancel expired orders
                if ((orderStatus == "NEW" || orderStatus == "PARTIALLY_FILLED") && storedDate.CompareTo(expirationTime) <= 0)
                {
                    _binanceService.CancelOrder(binanceOrder.symbol, binanceOrder.orderId);
                }

                if (orderStatus == "PARTIALLY_FILLED")
                {
                    orderService.UpdateSellOrderDb(order, binanceOrder, "");
                }

                if (orderStatus == "FILLED")
                {
                    // Get current AI prediction for filled sell order
                    double exitAiScore = 0;
                    string exitAiPrediction = "";
                    try
                    {
                        var mlPrediction = _mlService?.MLPredList?.FirstOrDefault(p => p.Symbol == order.Symbol);
                        if (mlPrediction != null && mlPrediction.Score != null && mlPrediction.Score.Length > 0)
                        {
                            exitAiScore = mlPrediction.Confidence;
                            exitAiPrediction = mlPrediction.PredictedLabel ?? string.Empty;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get exit AI prediction for {Symbol}", order.Symbol);
                    }

                    orderService.CloseOrderDb(order, binanceOrder, exitAiScore, exitAiPrediction);
                }

                if (orderStatus == "CANCELED" || orderStatus == "REJECTED" || orderStatus == "EXPIRED")
                {
                    orderService.UpdateSellOrderDb(order, binanceOrder, "");
                    orderService.RecycleOrderDb(order.Id);
                    _tradingState.OnHold.Remove(order.Symbol);
                }
            }

            _hub.Clients.All.SendAsync("refreshUI");
        }

        private async Task CheckBuyOrder(IOrderService orderService, ApplicationDbContext dbContext)
        {
            // Get pending buy orders from database
            var pendingBuyOrders = dbContext.Order
                .Where(p => p.Status != "FILLED" && p.Side == "BUY")
                .ToList();

            if (pendingBuyOrders.Count == 0)
                return;

            // Check status of each pending buy order
            foreach (var order in pendingBuyOrders)
            {
                var binanceOrder = _binanceService.OrderStatus(order.Symbol, order.BuyOrderId);
                var storedDate = DateTime.ParseExact(order.OpenDate, "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
                var expirationTime = DateTime.Now.AddSeconds(-50);
                var orderStatus = binanceOrder.status;

                // Cancel expired orders
                if ((orderStatus == "NEW" || orderStatus == "PARTIALLY_FILLED") && storedDate.CompareTo(expirationTime) <= 0)
                {
                    _binanceService.CancelOrder(binanceOrder.symbol, binanceOrder.orderId);
                }

                if (orderStatus == "FILLED" || orderStatus == "PARTIALLY_FILLED")
                {
                    orderService.UpdateBuyOrderDb(order, binanceOrder);
                }

                if (orderStatus == "CANCELED" || orderStatus == "REJECTED" || orderStatus == "EXPIRED")
                {
                    decimal.TryParse(binanceOrder.executedQty, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal executedQty);
                    if (executedQty == 0)
                    {
                        _tradingState.OnHold.Remove(order.Symbol);
                        orderService.DeleteOrderDb(order.Id);
                    }
                    else
                    {
                        orderService.UpdateBuyOrderDb(order, binanceOrder);
                        orderService.RecycleOrderDb(order.Id);
                    }
                }
            }

            await _hub.Clients.All.SendAsync("refreshUI");
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

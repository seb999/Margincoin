using System.Collections.Generic;
using MarginCoin.Class;
using MarginCoin.Model;
using MarginCoin.Configuration;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MarginCoin.Misc;
using System;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarginCoin.Service
{
    public interface IOrderService
    {
        void SaveHighLow(List<Candle> symbolCandles, Order activeOrder);
        List<Order> GetActiveOrder();
        void UpdateTakeProfit(List<Candle> symbolCandle, Order activeOrder, double takeProfitPercentage);
        void UpdateOrderPriceTracking(List<Candle> symbolCandles, Order activeOrder, double takeProfitPercentage);
        void UpdateStopLoss(List<Candle> symbolCandles, Order activeOrder);
        void SaveBuyOrderDb(MarketStream symbolSpot, List<Candle> symbolCandle, BinanceOrder binanceOrder, double aiScore = 0, string aiPrediction = "");
        void CloseOrderDb(Order dbOrder, BinanceOrder binanceOrder, double exitAiScore = 0, string exitAiPrediction = "");
        void DeleteOrderDb(double id);
        void UpdateBuyOrderDb(Order dbOrder, BinanceOrder binanceOrder);
        void UpdateSellOrderDb(Order dbOrder, BinanceOrder binanceOrder, string closeType);
        void RecycleOrderDb(double id);
        Task BuyLimit(MarketStream symbolSpot, List<Candle> symbolCandleList, double aiScore = 0, string aiPrediction = "");
        Task SellLimit(Order dbOrder, MarketStream symbolSpot, string closeType, double exitAiScore = 0, string exitAiPrediction = "");
        Task SellMarket(Order dbOrder, string closeType, double exitAiScore = 0, string exitAiPrediction = "");
        Task BuyMarket(MarketStream symbolSpot, List<Candle> symbolCandleList, double aiScore = 0, string aiPrediction = "");
    }

    public class OrderService : IOrderService
    {
    private readonly ApplicationDbContext _appDbContext;
    private readonly IBinanceService _binanceService;
    private readonly IHubContext<SignalRHub> _hub;
    private readonly ILogger _logger;
    private readonly ITradingState _tradingState;
    private readonly TradingConfiguration _config;
    private readonly ITradingSettingsService _settingsService;

    public OrderService(ApplicationDbContext appDbContext,
        IBinanceService binanceService,
        IHubContext<SignalRHub> hub,
        ILogger<OrderService> logger,
        ITradingState tradingState,
        IOptions<TradingConfiguration> tradingConfig,
        ITradingSettingsService settingsService)
    {
        _appDbContext = appDbContext;
        _binanceService = binanceService;
        _hub = hub;
        _logger = logger;
        _tradingState = tradingState;
        _config = tradingConfig.Value;
        _settingsService = settingsService;
    }

    private RuntimeTradingSettings GetRuntimeSettings()
    {
        return _settingsService.GetRuntimeSettingsAsync().GetAwaiter().GetResult();
    }

    #region trading methods

    public List<Order> GetActiveOrder()
    {
        try
        {
            return _appDbContext.Order.Where(p => p.IsClosed == 0).ToList();
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to get active orders");
            return new List<Order>();
        }
    }

    public void UpdateTakeProfit(List<Candle> symbolCandles, Order dbOrder, double takeProfitPercentage)
    {
        // Deprecated: Use UpdateOrderPriceTracking instead for atomic updates
        // Keeping for backward compatibility but logging warning
        _logger.LogWarning("UpdateTakeProfit called directly - consider using UpdateOrderPriceTracking for atomic updates");

        try
        {
            var currentHigh = Math.Max(symbolCandles.Last().c, symbolCandles.Last().h);
            if (currentHigh > dbOrder.HighPrice)
            {
                dbOrder.HighPrice = currentHigh;
                dbOrder.TakeProfit = currentHigh * (1 - (takeProfitPercentage / 100));
                _appDbContext.Order.Update(dbOrder);
                _appDbContext.SaveChanges();
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to update take profit for order {OrderId} symbol {Symbol}", dbOrder.Id, dbOrder.Symbol);
        }
    }

    public void SaveHighLow(List<Candle> symbolCandles, Order dbOrder)
    {
        // Deprecated: Use UpdateOrderPriceTracking instead for atomic updates
        // Keeping for backward compatibility but logging warning
        _logger.LogWarning("SaveHighLow called directly - consider using UpdateOrderPriceTracking for atomic updates");

        try
        {
            var lastCandle = symbolCandles.Last();
            dbOrder.ClosePrice = lastCandle.c;

            // Use candle high (h) for more accurate peak tracking
            var currentHigh = Math.Max(lastCandle.c, lastCandle.h);
            if (currentHigh > dbOrder.HighPrice)
            {
                dbOrder.HighPrice = currentHigh;
            }

            // Update Low
            if (lastCandle.c < dbOrder.LowPrice)
            {
                dbOrder.LowPrice = lastCandle.c;
            }

            _appDbContext.Order.Update(dbOrder);
            _appDbContext.SaveChanges();
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to save high/low for order {OrderId} symbol {Symbol}", dbOrder.Id, dbOrder.Symbol);
        }
    }

    public void UpdateOrderPriceTracking(List<Candle> symbolCandles, Order dbOrder, double takeProfitPercentage)
    {
        try
        {
            var lastCandle = symbolCandles.Last();

            // Always update close price
            dbOrder.ClosePrice = lastCandle.c;

            // Use candle high (h) for accurate peak tracking - prevents missing intra-candle highs
            var currentHigh = Math.Max(lastCandle.c, lastCandle.h);

            // Update HighPrice and TakeProfit atomically when new high is reached
            if (currentHigh > dbOrder.HighPrice)
            {
                dbOrder.HighPrice = currentHigh;
                dbOrder.TakeProfit = currentHigh * (1 - (takeProfitPercentage / 100));
                _logger.LogDebug("Updated HighPrice to {High} and TakeProfit to {TP} for {Symbol}",
                    currentHigh, dbOrder.TakeProfit, dbOrder.Symbol);
            }

            // Update LowPrice
            if (lastCandle.c < dbOrder.LowPrice)
            {
                dbOrder.LowPrice = lastCandle.c;
            }

            // Single atomic save - prevents race conditions
            _appDbContext.Order.Update(dbOrder);
            _appDbContext.SaveChanges();
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to update order price tracking for {OrderId} symbol {Symbol}",
                dbOrder.Id, dbOrder.Symbol);
        }
    }

    public void UpdateStopLoss(List<Candle> symbolCandles, Order dbOrder)
    {
        try
        {
            var runtime = GetRuntimeSettings();
            if (!runtime.EnableDynamicStopLoss)
            {
                return;
            }

            // Calculate the current market price
            Candle lastCandle = symbolCandles.Select(p => p).LastOrDefault();

            // Trailing stop loss: continuously adjust stop loss based on highest price reached
            // The stop loss trails below the high price by the TrailingStopPercentage
            var trailingStopPrice = dbOrder.HighPrice * (1 - runtime.TrailingStopPercentage / 100);

            // Calculate current profit metrics for logging
            var currentPrice = lastCandle?.c ?? 0;
            var currentProfit = (currentPrice - dbOrder.OpenPrice) * dbOrder.QuantityBuy;
            var currentProfitPct = ((currentPrice - dbOrder.OpenPrice) / dbOrder.OpenPrice) * 100;
            var maxPotentialProfit = (dbOrder.HighPrice - dbOrder.OpenPrice) * dbOrder.QuantityBuy;
            var maxPotentialProfitPct = ((dbOrder.HighPrice - dbOrder.OpenPrice) / dbOrder.OpenPrice) * 100;

            // Only move stop loss UP, never down (this locks in profits)
            if (trailingStopPrice > dbOrder.StopLose)
            {
                var oldStopLoss = dbOrder.StopLose;
                dbOrder.StopLose = trailingStopPrice;
                _appDbContext.Order.Update(dbOrder);
                _appDbContext.SaveChanges();

                _logger.LogInformation(
                    "Trailing stop loss updated for {Symbol} - OrderId: {OrderId}. " +
                    "Entry: {Entry:F2}, Current: {Current:F2}, High: {High:F2} | " +
                    "Stop: {OldStop:F2} → {NewStop:F2} ({TrailingPct}% trail) | " +
                    "Current P/L: {CurrentProfit:F2} ({CurrentProfitPct:F2}%), Max P/L: {MaxProfit:F2} ({MaxProfitPct:F2}%)",
                    dbOrder.Symbol, dbOrder.Id,
                    dbOrder.OpenPrice, currentPrice, dbOrder.HighPrice,
                    oldStopLoss, trailingStopPrice, runtime.TrailingStopPercentage,
                    currentProfit, currentProfitPct, maxPotentialProfit, maxPotentialProfitPct);
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to update stop loss for order {OrderId} symbol {Symbol}", dbOrder.Id, dbOrder.Symbol);
        }
    }

    #endregion

    #region order check/update/delete

    public void SaveBuyOrderDb(MarketStream symbolSpot, List<Candle> symbolCandle, BinanceOrder binanceOrder, double aiScore = 0, string aiPrediction = "")
    {
        _logger.LogInformation("Opening trade for {Symbol} - OrderId: {OrderId}", binanceOrder.symbol, binanceOrder.orderId);
        var runtime = GetRuntimeSettings();
        var entryTrendScore = TradeHelper.CalculateTrendScore(symbolCandle, _config.UseWeightedTrendScore);
        var openPrice = TradeHelper.CalculateAvragePrice(binanceOrder);
        Order myOrder = new Order
        {
            BuyOrderId = binanceOrder.orderId,
            SellOrderId = 0,
            Status = binanceOrder.status,
            Side = binanceOrder.side,
            OrderDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
            OpenDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
            OpenPrice = openPrice,
            LowPrice = openPrice,
            TakeProfit = openPrice * (1 - (runtime.TakeProfitPercentage / 100)),
            StopLose = openPrice * (1 - (runtime.StopLossPercentage / 100)),
            ClosePrice = 0,
            HighPrice = openPrice,
            Volume = symbolSpot.v,
            QuantityBuy = Helper.ToDouble(binanceOrder.executedQty),
            QuantitySell = 0,
            IsClosed = 0,
            Fee = _tradingState.IsProd ? binanceOrder.fills.Sum(p => Helper.ToDouble(p.commission)) : Math.Round((TradeHelper.CalculateAvragePrice(binanceOrder) * Helper.ToDouble(binanceOrder.executedQty)) / 100) * 0.1,
            Symbol = binanceOrder.symbol,
            ATR = symbolCandle.Last().ATR,
            RSI = symbolCandle.Last().Rsi,
            EMA = symbolCandle.Last().Ema,
            StochSlowD = symbolCandle.Last().StochSlowD,
            StochSlowK = symbolCandle.Last().StochSlowK,
            MACD = symbolCandle.Last().Macd,
            MACDSign = symbolCandle.Last().MacdSign,
            MACDHist = symbolCandle.Last().MacdHist,
            TrendScore = entryTrendScore,
            AIScore = aiScore,
            AIPrediction = aiPrediction,
        };

        _appDbContext.Order.Add(myOrder);
        _appDbContext.SaveChanges();
    }

    public void SaveSellOrderDb(Order dbOrder, BinanceOrder binanceOrder, string closeType)
    {
        dbOrder.SellOrderId = binanceOrder.orderId;
        dbOrder.OrderDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
        dbOrder.Status = binanceOrder.status;
        dbOrder.Side = binanceOrder.side;
        dbOrder.ClosePrice = TradeHelper.CalculateAvragePrice(binanceOrder);
        dbOrder.QuantitySell = Helper.ToDouble(binanceOrder.executedQty);
        if (closeType != "") dbOrder.Type = closeType;

        _appDbContext.Order.Update(dbOrder);
        _appDbContext.SaveChanges();
    }

    public void UpdateSellOrderDb(Order dbOrder, BinanceOrder binanceOrder, string closeType)
    {
        try
        {
            dbOrder.Status = binanceOrder.status;
            dbOrder.ClosePrice = TradeHelper.CalculateAvragePrice(binanceOrder);
            dbOrder.QuantitySell = Helper.ToDouble(binanceOrder.executedQty);
            if (closeType != "") dbOrder.Type = closeType;

            _appDbContext.Order.Update(dbOrder);
            _appDbContext.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update sell order for {Symbol}", dbOrder.Symbol);
        }
    }

    public void UpdateBuyOrderDb(Order dbOrder, BinanceOrder binanceOrder)
    {
        var runtime = GetRuntimeSettings();
        var orderPrice = TradeHelper.CalculateAvragePrice(binanceOrder);

        dbOrder.Status = binanceOrder.status;
        dbOrder.OpenPrice = orderPrice;
        dbOrder.LowPrice = orderPrice;
        dbOrder.TakeProfit = orderPrice * (1 - (runtime.TakeProfitPercentage / 100));
        dbOrder.StopLose = orderPrice * (1 - (runtime.StopLossPercentage / 100));
        dbOrder.QuantityBuy = Helper.ToDouble(binanceOrder.executedQty);

        _appDbContext.Order.Update(dbOrder);
        _appDbContext.SaveChanges();
    }

    public void DeleteOrderDb(double id)
    {
        try
        {
            Order myOrder = _appDbContext.Order.SingleOrDefault(p => p.Id == id);
            if (myOrder != null)
            {
                _appDbContext.Order.Remove(myOrder);
                _appDbContext.SaveChanges();
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to delete order {OrderId}", id);
        }
    }

    public void RecycleOrderDb(double id)
    {
        Order myOrder = _appDbContext.Order.Where(p => p.Id == id).FirstOrDefault();
        myOrder.Type = "";
        myOrder.Status = "FILLED";
        myOrder.Side = "BUY";
        _appDbContext.Order.Update(myOrder);
        _appDbContext.SaveChanges();
    }

    public void CloseOrderDb(Order dbOrder, BinanceOrder binanceOrder, double exitAiScore = 0, string exitAiPrediction = "")
    {
        //_appDbContext.Entry(dbOrder).Reload();

        if (!_tradingState.OnHold.ContainsKey(dbOrder.Symbol)) _tradingState.OnHold.Add(dbOrder.Symbol, true);
        dbOrder.Status = binanceOrder.status;
        dbOrder.ClosePrice = TradeHelper.CalculateAvragePrice(binanceOrder);
        dbOrder.QuantitySell = Helper.ToDouble(binanceOrder.executedQty);
        dbOrder.Profit = Math.Round((dbOrder.ClosePrice - dbOrder.OpenPrice) * dbOrder.QuantitySell);
        dbOrder.IsClosed = 1;
        dbOrder.CloseDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

        // Store exit AI prediction values
        dbOrder.ExitAIScore = exitAiScore;
        dbOrder.ExitAIPrediction = exitAiPrediction;

        _appDbContext.Order.Update(dbOrder);
        _appDbContext.SaveChanges();
    }

    #endregion

    #region binance order methods

    public async Task BuyLimit(MarketStream symbolSpot, List<Candle> symbolCandleList, double aiScore = 0, string aiPrediction = "")
    {
        var runtime = GetRuntimeSettings();
        // Check available USDC balance before placing order
        var availableBalance = GetAvailableUSDCBalance();
        if (availableBalance < runtime.QuoteOrderQty)
        {
            _logger.LogWarning("Insufficient USDC balance for {Symbol}. Required: {Required} USDC, Available: {Available} USDC. Skipping trade.",
                symbolSpot.s, runtime.QuoteOrderQty, availableBalance);
            _tradingState.OnHold.Remove(symbolSpot.s);
            return;
        }

        var (nbrDecimalPrice, nbrDecimalQty) = GetOrderParameters(symbolSpot);
        var price = Math.Round(symbolSpot.c * (1 + _config.OrderOffset / 100), nbrDecimalPrice);
        var qty = Math.Round(runtime.QuoteOrderQty / price, nbrDecimalQty);

        var myBinanceOrder = _binanceService.BuyLimit(symbolSpot.s, qty, price, MyEnum.TimeInForce.GTC);
        if (myBinanceOrder == null)
        {
            _tradingState.OnHold.Remove(symbolSpot.s);
            return;
        }

        SaveBuyOrderDb(symbolSpot, symbolCandleList, myBinanceOrder, aiScore, aiPrediction);

        await Task.Delay(300);
        myBinanceOrder.price = TradeHelper.CalculateAvragePrice(myBinanceOrder).ToString();
        await _hub.Clients.All.SendAsync("newPendingOrder", JsonSerializer.Serialize(myBinanceOrder));
        await _hub.Clients.All.SendAsync("refreshUI");
    }

    public async Task SellLimit(Order dbOrder, MarketStream symbolSpot, string closeType, double exitAiScore = 0, string exitAiPrediction = "")
    {
        //we exit in the buy order is still pending
        if (dbOrder.Status != "FILLED")
            return;

        var (nbrDecimalPrice, nbrDecimalQty) = GetOrderParameters(symbolSpot);
        var price = Math.Round(symbolSpot.c * (1 - _config.OrderOffset / 100), nbrDecimalPrice);
        var qty = Math.Round(dbOrder.QuantityBuy - dbOrder.QuantitySell, nbrDecimalQty);

        var myBinanceOrder = _binanceService.SellLimit(dbOrder.Symbol, qty, price, MyEnum.TimeInForce.GTC);
        if (myBinanceOrder == null)
            return;

        SaveSellOrderDb(dbOrder, myBinanceOrder, closeType);

        if (myBinanceOrder.status == "FILLED")
            CloseOrderDb(dbOrder, myBinanceOrder, exitAiScore, exitAiPrediction);

        await Task.Delay(300);
        myBinanceOrder.price = TradeHelper.CalculateAvragePrice(myBinanceOrder).ToString();
        await _hub.Clients.All.SendAsync("sellOrderFilled", JsonSerializer.Serialize(myBinanceOrder));
    }

    public async Task BuyMarket(MarketStream symbolSpot, List<Candle> symbolCandleList, double aiScore = 0, string aiPrediction = "")
    {
        var runtime = GetRuntimeSettings();
        // Check available USDC balance before placing order
        var availableBalance = GetAvailableUSDCBalance();
        if (availableBalance < runtime.QuoteOrderQty)
        {
            _logger.LogWarning("Insufficient USDC balance for {Symbol}. Required: {Required} USDC, Available: {Available} USDC. Skipping trade.",
                symbolSpot.s, runtime.QuoteOrderQty, availableBalance);
            _tradingState.OnHold.Remove(symbolSpot.s);
            return;
        }

        BinanceOrder myBinanceOrder = _binanceService.BuyMarket(symbolSpot.s, runtime.QuoteOrderQty);

        if (myBinanceOrder == null)
        {
            _tradingState.OnHold.Remove(symbolSpot.s);
            return;
        }

        if (myBinanceOrder.status == "EXPIRED")
        {
            _tradingState.OnHold.Remove(symbolSpot.s);
            _logger.LogWarning($"Call {MyEnum.BinanceApiCall.BuyMarket} {symbolSpot.s} Order status Expired");
            return;
        }

        int i = 0;
        while (myBinanceOrder.status != "FILLED" && i < 5)
        {
            myBinanceOrder = _binanceService.OrderStatus(myBinanceOrder.symbol, myBinanceOrder.orderId);
            i++;
        }

        if (myBinanceOrder.status == "FILLED")
        {
            SaveBuyOrderDb(symbolSpot, symbolCandleList, myBinanceOrder, aiScore, aiPrediction);

            _tradingState.OnHold.Remove(symbolSpot.s);
            myBinanceOrder.price = TradeHelper.CalculateAvragePrice(myBinanceOrder).ToString();
            await _hub.Clients.All.SendAsync("newPendingOrder", JsonSerializer.Serialize(myBinanceOrder));
            await Task.Delay(500);
            await _hub.Clients.All.SendAsync("refreshUI");
        }
    }

    public async Task SellMarket(Order dbOrder, string closeType, double exitAiScore = 0, string exitAiPrediction = "")
    {
        BinanceOrder myBinanceOrder = _binanceService.SellMarket(dbOrder.Symbol, dbOrder.QuantityBuy - dbOrder.QuantitySell);

        if (myBinanceOrder == null)
            return;

        int i = 0;
        while (myBinanceOrder.status != MyEnum.OrderStatus.FILLED.ToString() && i < 5)
        {
            await Task.Delay(50);
            myBinanceOrder = _binanceService.OrderStatus(myBinanceOrder.symbol, myBinanceOrder.orderId);
            i++;
        }

        if (myBinanceOrder.status == MyEnum.OrderStatus.EXPIRED.ToString())
        {
            _logger.LogWarning($"Call {MyEnum.BinanceApiCall.SellLimit} {dbOrder.Symbol} Expired");
        }

        if (myBinanceOrder.status == MyEnum.OrderStatus.FILLED.ToString())
        {
            SaveSellOrderDb(dbOrder, myBinanceOrder, closeType);
            CloseOrderDb(dbOrder, myBinanceOrder, exitAiScore, exitAiPrediction);
            await Task.Delay(500);
            await _hub.Clients.All.SendAsync("sellOrderFilled", JsonSerializer.Serialize(myBinanceOrder));
        }
    }

    #endregion

    #region helper

    private (int nbrDecimalPrice, int nbrDecimalQty) GetOrderParameters(MarketStream symbolSpot)
    {
        var ticker = _binanceService.Ticker(symbolSpot.s);
        var nbrDecimalPrice = Helper.GetNumberDecimal(ticker.symbols[0].filters[0].tickSize);
        var nbrDecimalQty = Helper.GetNumberDecimal(ticker.symbols[0].filters[1].stepSize);
        return (nbrDecimalPrice, nbrDecimalQty);
    }

    private double GetAvailableUSDCBalance()
    {
        try
        {
            var account = _binanceService.Account();
            if (account?.balances == null)
            {
                _logger.LogWarning("Failed to retrieve account balance - account or balances is null");
                return 0;
            }

            var usdcBalance = account.balances.FirstOrDefault(b => b.asset == "USDC");
            if (usdcBalance != null && double.TryParse(usdcBalance.free, NumberStyles.Any, CultureInfo.InvariantCulture, out double balance))
            {
                return balance;
            }

            _logger.LogWarning("USDC balance not found in account");
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve USDC balance");
            return 0;
        }
    }

    #endregion

}
}

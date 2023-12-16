using System.Collections.Generic;
using MarginCoin.Class;
using MarginCoin.Model;
using MarginCoin.Service;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MarginCoin.Misc;
using System;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

public class OrderService : IOrderService
{
    private ApplicationDbContext _appDbContext;
    private IBinanceService _binanceService;
    private IHubContext<SignalRHub> _hub;
    private ILogger _logger;

    public OrderService(ApplicationDbContext appDbContext,
        IBinanceService binanceService,
        IHubContext<SignalRHub> hub,
        ILogger<OrderService> logger)
    {
        _appDbContext = appDbContext;
        _binanceService = binanceService;
        _hub = hub;
        _logger = logger;
    }

    #region trading methods

    public List<Order> GetActiveOrder()
    {
        try
        {
            return _appDbContext.Order.Where(p => p.IsClosed == 0).ToList();
        }
        catch (System.Exception)
        {
            return new List<Order>();
        }
    }

    public void UpdateTakeProfit(List<Candle> symbolCandles, Order dbOrder, double takeProfitPercentage)
    {
        try
        {
            if (symbolCandles.Last().c > dbOrder.HighPrice)
            {
                dbOrder.TakeProfit = dbOrder.HighPrice * (1 - (takeProfitPercentage / 100));
                _appDbContext.Order.Update(dbOrder);
                _appDbContext.SaveChanges();
            }
        }
        catch (System.Exception)
        {
            return;
        }

    }

    public void SaveHighLow(List<Candle> symbolCandles, Order dbOrder)
    {
        try
        {
            dbOrder.ClosePrice = symbolCandles.Last().c;

            //Update High
            if (symbolCandles.Last().c > dbOrder.HighPrice)
            {
                dbOrder.HighPrice = symbolCandles.Last().c;
            }

            //Update Low
            if (symbolCandles.Last().c < dbOrder.LowPrice)
            {
                dbOrder.LowPrice = symbolCandles.Last().c;
            }

            _appDbContext.Order.Update(dbOrder);
            _appDbContext.SaveChanges();
        }
        catch (System.Exception)
        {
            return;
        }
    }

    public void UpdateStopLoss(List<Candle> symbolCandles, Order dbOrder)
    {
        try
        {
            // Calculate the current market price
            Candle lastCandle = symbolCandles.Select(p => p).LastOrDefault();

            //If price up 0.4% we move the stop lose to open price
            if (lastCandle.c > dbOrder.OpenPrice * 1.004)
            {
                // Update the stop loss price of the active order
                dbOrder.StopLose = dbOrder.OpenPrice;
                _appDbContext.Order.Update(dbOrder);
                _appDbContext.SaveChanges();
            }
        }
        catch (System.Exception)
        {
            return;
        }
    }

    #endregion

    #region order check/update/delete

    public void SaveBuyOrderDb(MarketStream symbolSpot, List<Candle> symbolCandle, BinanceOrder binanceOrder)
    {
        Console.WriteLine("Open trade");
        Order myOrder = new Order
        {
            BuyOrderId = binanceOrder.orderId,
            SellOrderId = 0,
            Status = binanceOrder.status,
            Side = binanceOrder.side,
            OrderDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
            OpenDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
            OpenPrice = TradeHelper.CalculateAvragePrice(binanceOrder),
            LowPrice = TradeHelper.CalculateAvragePrice(binanceOrder),
            TakeProfit = TradeHelper.CalculateAvragePrice(binanceOrder) * (1 - (Global.takeProfitPercentage / 100)),
            StopLose = TradeHelper.CalculateAvragePrice(binanceOrder) * (1 - (Global.stopLossPercentage / 100)),
            ClosePrice = 0,
            HighPrice = 0,
            Volume = symbolSpot.v,
            QuantityBuy = Helper.ToDouble(binanceOrder.executedQty),
            QuantitySell = 0,
            IsClosed = 0,
            Fee = Global.isProd ? binanceOrder.fills.Sum(p => Helper.ToDouble(p.commission)) : Math.Round((TradeHelper.CalculateAvragePrice(binanceOrder) * Helper.ToDouble(binanceOrder.executedQty)) / 100) * 0.1,
            Symbol = binanceOrder.symbol,
            ATR = symbolCandle.Last().ATR,
            RSI = symbolCandle.Last().Rsi,
            EMA = symbolCandle.Last().Ema,
            StochSlowD = symbolCandle.Last().StochSlowD,
            StochSlowK = symbolCandle.Last().StochSlowK,
            MACD = symbolCandle.Last().Macd,
            MACDSign = symbolCandle.Last().MacdSign,
            MACDHist = symbolCandle.Last().MacdHist,
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
            Console.WriteLine(ex);
        }
    }

    public void UpdateBuyOrderDb(Order dbOrder, BinanceOrder binanceOrder)
    {
        var orderPrice = TradeHelper.CalculateAvragePrice(binanceOrder);

        dbOrder.Status = binanceOrder.status;
        dbOrder.OpenPrice = orderPrice;
        dbOrder.LowPrice = orderPrice;
        dbOrder.TakeProfit = orderPrice * (1 - (Global.takeProfitPercentage / 100));
        dbOrder.StopLose = orderPrice * (1 - (Global.stopLossPercentage / 100));
        dbOrder.QuantityBuy = Helper.ToDouble(binanceOrder.executedQty);

        _appDbContext.Order.Update(dbOrder);
        _appDbContext.SaveChanges();
    }

    public void DeleteOrderDb(double id)
    {
        try
        {
            Order myOrder = _appDbContext.Order.SingleOrDefault(p => p.Id == id);
            _appDbContext.Order.Remove(myOrder);
            _appDbContext.SaveChanges();
        }
        catch (System.Exception ex)
        {
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

    public void CloseOrderDb(Order dbOrder, BinanceOrder binanceOrder)
    {
        //_appDbContext.Entry(dbOrder).Reload();

        //Global.isDbBusy = true;
        if (!Global.onHold.ContainsKey(dbOrder.Symbol)) Global.onHold.Add(dbOrder.Symbol, true);
        dbOrder.Status = binanceOrder.status;
        dbOrder.ClosePrice = TradeHelper.CalculateAvragePrice(binanceOrder);
        dbOrder.QuantitySell = Helper.ToDouble(binanceOrder.executedQty);
        dbOrder.Profit = Math.Round((dbOrder.ClosePrice - dbOrder.OpenPrice) * dbOrder.QuantitySell);
        dbOrder.IsClosed = 1;
        dbOrder.CloseDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
        _appDbContext.Order.Update(dbOrder);
        _appDbContext.SaveChanges();
        //Global.isDbBusy = false;
    }

    #endregion

    #region binance order methods

    public void BuyLimit(MarketStream symbolSpot, List<Candle> symbolCandleList)
    {
        var (nbrDecimalPrice, nbrDecimalQty) = GetOrderParameters(symbolSpot);
        var price = Math.Round(symbolSpot.c * (1 + Global.orderOffset / 100), nbrDecimalPrice);
        var qty = Math.Round(Global.quoteOrderQty / price, nbrDecimalQty);

        var myBinanceOrder = _binanceService.BuyLimit(symbolSpot.s, qty, price, MyEnum.TimeInForce.GTC);
        if (myBinanceOrder == null)
        {
            Global.onHold.Remove(symbolSpot.s);
            return;
        }

        SaveBuyOrderDb(symbolSpot, symbolCandleList, myBinanceOrder);

        Task.Delay(300);
        myBinanceOrder.price = TradeHelper.CalculateAvragePrice(myBinanceOrder).ToString();
        _hub.Clients.All.SendAsync("newPendingOrder", JsonSerializer.Serialize(myBinanceOrder));
        _hub.Clients.All.SendAsync("refreshUI");
    }

    public void SellLimit(Order dbOrder, MarketStream symbolSpot, string closeType)
    {
        //we exit in the buy order is still pending
        if (dbOrder.Status != "FILLED")
            return;

        var (nbrDecimalPrice, nbrDecimalQty) = GetOrderParameters(symbolSpot);
        var price = Math.Round(symbolSpot.c * (1 - Global.orderOffset / 100), nbrDecimalPrice);
        var qty = Math.Round(dbOrder.QuantityBuy - dbOrder.QuantitySell, nbrDecimalQty);

        var myBinanceOrder = _binanceService.SellLimit(dbOrder.Symbol, qty, price, MyEnum.TimeInForce.GTC);
        if (myBinanceOrder == null)
            return;

        SaveSellOrderDb(dbOrder, myBinanceOrder, closeType);

        if (myBinanceOrder.status == "FILLED")
            CloseOrderDb(dbOrder, myBinanceOrder);

        Task.Delay(300);
        myBinanceOrder.price = TradeHelper.CalculateAvragePrice(myBinanceOrder).ToString();
        _hub.Clients.All.SendAsync("sellOrderFilled", JsonSerializer.Serialize(myBinanceOrder)); 
    }

    public async Task BuyMarket(MarketStream symbolSpot, List<Candle> symbolCandleList)
    {
        BinanceOrder myBinanceOrder = _binanceService.BuyMarket(symbolSpot.s, Global.quoteOrderQty);

        if (myBinanceOrder == null)
        {
            Global.onHold.Remove(symbolSpot.s);
            return;
        }

        if (myBinanceOrder.status == "EXPIRED")
        {
            Global.onHold.Remove(symbolSpot.s);
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
            SaveBuyOrderDb(symbolSpot, symbolCandleList, myBinanceOrder);

            Global.onHold.Remove(symbolSpot.s);
            myBinanceOrder.price = TradeHelper.CalculateAvragePrice(myBinanceOrder).ToString();
            await _hub.Clients.All.SendAsync("newPendingOrder", JsonSerializer.Serialize(myBinanceOrder));
            await Task.Delay(500);
            await _hub.Clients.All.SendAsync("refreshUI");
        }
    }

    public async Task SellMarket(Order dbOrder, string closeType)
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
            CloseOrderDb(dbOrder, myBinanceOrder);
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

    #endregion

}
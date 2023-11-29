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
    private readonly ApplicationDbContext _appDbContext;
    private IBinanceService _binanceService;
    private IHubContext<SignalRHub> _hub;
    private ILogger _logger;

    public OrderService([FromServices] ApplicationDbContext appDbContext,
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
        return _appDbContext.Order.Where(p => p.IsClosed == 0).ToList();
    }

    public void UpdateTakeProfit(List<Candle> symbolCandles, Order activeOrder, double takeProfitPercentage)
    {
        if (symbolCandles.Last().c > activeOrder.HighPrice)
        {
            activeOrder.TakeProfit = activeOrder.HighPrice * (1 - (takeProfitPercentage / 100));
            _appDbContext.Order.Update(activeOrder);
            _appDbContext.SaveChanges();
        }
    }

    public void SaveHighLow(List<Candle> symbolCandles, Order activeOrder)
    {
        //Save current value
        activeOrder.ClosePrice = symbolCandles.Last().c;

        //Save High
        if (symbolCandles.Last().c > activeOrder.HighPrice)
        {
            activeOrder.HighPrice = symbolCandles.Last().c;
            _hub.Clients.All.SendAsync("refreshUI");
        }

        //Save Low
        if (symbolCandles.Last().c < activeOrder.LowPrice)
        {
            activeOrder.LowPrice = symbolCandles.Last().c;
        }

        _appDbContext.Order.Update(activeOrder);
        _appDbContext.SaveChanges();
    }

    public void UpdateStopLoss(List<Candle> symbolCandles, Order activeOrder)
    {
        // Calculate the current market price
        Candle lastCandle = symbolCandles.Select(p => p).LastOrDefault();

        //If price up 0.4% we move the stop lose to open price
        if (lastCandle.c > activeOrder.OpenPrice * 1.004)
        {
            // Update the stop loss price of the active order
            activeOrder.StopLose = activeOrder.OpenPrice;
            _appDbContext.Order.Update(activeOrder);
            _appDbContext.SaveChanges();
        }
    }

    #endregion

    #region order check/update/delete
    public void SaveOrderDb(MarketStream symbolSpot, List<Candle> symbolCandle, BinanceOrder binanceOrder)
    {
        Console.WriteLine("Open trade");
        Order myOrder = new Order
        {
            OrderId = binanceOrder.orderId,
            Status = binanceOrder.status,
            Side = binanceOrder.side,
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

    public void UpdateSellOrderDb(double id, BinanceOrder binanceOrder, string closeType)
    {
        Order myOrder = _appDbContext.Order.Where(p => p.Id == id).FirstOrDefault();
        myOrder.OrderId = binanceOrder.orderId;
        myOrder.Status = binanceOrder.status;
        myOrder.Side = binanceOrder.side;
        myOrder.ClosePrice = TradeHelper.CalculateAvragePrice(binanceOrder);
        myOrder.QuantitySell = Helper.ToDouble(binanceOrder.executedQty);
        if (closeType != "") myOrder.Type = closeType;

        _appDbContext.Order.Update(myOrder);
        _appDbContext.SaveChanges();
    }

    public void UpdateBuyOrderDb(double id, BinanceOrder binanceOrder)
    {
        Order myOrder = _appDbContext.Order.Where(p => p.Id == id).FirstOrDefault();
        myOrder.Status = binanceOrder.status;

        var orderPrice = TradeHelper.CalculateAvragePrice(binanceOrder);

        myOrder.OpenPrice = orderPrice;
        myOrder.OpenPrice = orderPrice;
        myOrder.LowPrice = orderPrice;
        myOrder.TakeProfit = orderPrice * (1 - (Global.takeProfitPercentage / 100));
        myOrder.StopLose = orderPrice * (1 - (Global.stopLossPercentage / 100));
        myOrder.QuantityBuy = Helper.ToDouble(binanceOrder.executedQty);

        _appDbContext.Order.Update(myOrder);
        _appDbContext.SaveChanges();
    }

    public void RecycleOrderDb(double id)
    {
        Order myOrder = _appDbContext.Order.Where(p => p.Id == id).FirstOrDefault();
        myOrder.Type = "";
        myOrder.Status = "FILLED";
        _appDbContext.Order.Update(myOrder);
        _appDbContext.SaveChanges();
    }

    public void CloseOrderDb(double id, BinanceOrder binanceOrder)
    {
        Order myOrder = _appDbContext.Order.Where(p => p.Id == id).Select(p => p).FirstOrDefault();
        if (!Global.onHold.ContainsKey(myOrder.Symbol)) Global.onHold.Add(myOrder.Symbol, true);
        myOrder.Status = binanceOrder.status;
        myOrder.ClosePrice = TradeHelper.CalculateAvragePrice(binanceOrder);
        myOrder.QuantitySell = binanceOrder.fills.Sum(fill => Helper.ToDouble(fill.qty));
        myOrder.Profit = Math.Round((myOrder.ClosePrice - myOrder.OpenPrice) * myOrder.QuantitySell);
        myOrder.IsClosed = 1;
        myOrder.CloseDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
        _appDbContext.Order.Update(myOrder);
        _appDbContext.SaveChanges();
    }

    #endregion

    #region binance order methods

    public async Task BuyLimit(MarketStream symbolSpot, List<Candle> symbolCandleList)
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

        SaveOrderDb(symbolSpot, symbolCandleList, myBinanceOrder);

        myBinanceOrder.price = TradeHelper.CalculateAvragePrice(myBinanceOrder).ToString();
        await _hub.Clients.All.SendAsync("newPendingOrder", JsonSerializer.Serialize(myBinanceOrder));
        await Task.Delay(100);
        await _hub.Clients.All.SendAsync("refreshUI");
    }

    public async Task SellLimit(double id, List<MarketStream> marketStreamOnSpot, string closeType)
    {
        var myOrder = _appDbContext.Order.SingleOrDefault(p => p.Id == id);
        var symbolSpot = marketStreamOnSpot.SingleOrDefault(p => p.s == myOrder.Symbol);
        var (nbrDecimalPrice, nbrDecimalQty) = GetOrderParameters(symbolSpot);
        var price = Math.Round(symbolSpot.c * (1 - Global.orderOffset / 100), nbrDecimalPrice);
        var qty = Math.Round(myOrder.QuantityBuy - myOrder.QuantitySell, nbrDecimalQty);

        //we exit in the buy order is still pending
        if (myOrder.Status != "FILLED")
            return;

        var myBinanceOrder = _binanceService.SellLimit(myOrder.Symbol, qty, price, MyEnum.TimeInForce.GTC);
        if (myBinanceOrder == null)
            return;

        UpdateSellOrderDb(id, myBinanceOrder, closeType);

        if (myBinanceOrder.status == "FILLED")
            CloseOrderDb(id, myBinanceOrder);

        await Task.Delay(500);
        await _hub.Clients.All.SendAsync("sellOrderFilled", JsonSerializer.Serialize(myBinanceOrder)); //it is not really filled here, maybe not filled but we inform UI of new order                                                                                                //Call ForceGarbageOrder here
        await Task.Delay(500);
        await _hub.Clients.All.SendAsync("refreshUI");
    }

    public async void BuyMarket(MarketStream symbolSpot, List<Candle> symbolCandleList)
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
            SaveOrderDb(symbolSpot, symbolCandleList, myBinanceOrder);

            Global.onHold.Remove(symbolSpot.s);
            myBinanceOrder.price = TradeHelper.CalculateAvragePrice(myBinanceOrder).ToString();
            await _hub.Clients.All.SendAsync("newPendingOrder", JsonSerializer.Serialize(myBinanceOrder));
            await Task.Delay(500);
            await _hub.Clients.All.SendAsync("refreshUI");
        }
    }

    public async void SellMarket(double id, string closeType)
    {
        Order myOrder = _appDbContext.Order.SingleOrDefault(p => p.Id == id);
        BinanceOrder myBinanceOrder = _binanceService.SellMarket(myOrder.Symbol, myOrder.QuantityBuy - myOrder.QuantitySell);

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
            _logger.LogWarning($"Call {MyEnum.BinanceApiCall.SellLimit} {myOrder.Symbol} Expired");
        }

        if (myBinanceOrder.status == MyEnum.OrderStatus.FILLED.ToString())
        {
            UpdateSellOrderDb(id,myBinanceOrder,closeType);
            CloseOrderDb(id, myBinanceOrder);
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
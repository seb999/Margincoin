using System.Collections.Generic;
using MarginCoin.Class;
using MarginCoin.Model;
using MarginCoin.Service;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MarginCoin.Misc;
using System;

public class RepositoryService : IOrderService
{
    private readonly ApplicationDbContext _appDbContext;

    public RepositoryService([FromServices] ApplicationDbContext appDbContext)
    {
        _appDbContext = appDbContext;
    }

    #region market helper

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
        }

        //Save Low
        if (symbolCandles.Last().c < activeOrder.LowPrice)
        {
            activeOrder.LowPrice = symbolCandles.Last().c;
        }

        _appDbContext.Order.Update(activeOrder);
        _appDbContext.SaveChanges();
    }

    public List<Order> GetActiveOrder()
    {
        return _appDbContext.Order.Where(p => p.IsClosed == 0).ToList();
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

    public void SaveOrderDb(MarketStream symbolSpot, List<Candle> symbolCandle, BinanceOrder binanceOrder)
    {
        Console.WriteLine("Open trade");
        Order myOrder = new Order
        {
            OrderId = binanceOrder.orderId,
            Status = binanceOrder.status,
            OpenDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
            OpenPrice = TradeHelper.CalculateAvragePrice(binanceOrder),
            HighPrice = 0,
            LowPrice = TradeHelper.CalculateAvragePrice(binanceOrder),
            ClosePrice = 0,
            Volume = symbolSpot.v,
            TakeProfit = TradeHelper.CalculateAvragePrice(binanceOrder) * (1 - (Global.takeProfitPercentage / 100)),
            StopLose = TradeHelper.CalculateAvragePrice(binanceOrder) * (1 - (Global.stopLossPercentage / 100)),
            QuantityBuy = Helper.ToDouble(binanceOrder.executedQty),
            IsClosed = 0,
            Fee = Global.isProd ? binanceOrder.fills.Sum(p => Helper.ToDouble(p.commission)) : Math.Round((TradeHelper.CalculateAvragePrice(binanceOrder) * Helper.ToDouble(binanceOrder.executedQty)) / 100) * 0.1,
            Symbol = binanceOrder.symbol,
            ATR = symbolCandle.Last().ATR,
            RSI = symbolCandle.Last().Rsi,
            RSI_1 = symbolCandle[symbolCandle.Count - 2].Rsi,
            RSI_2 = symbolCandle[symbolCandle.Count - 3].Rsi,
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

    public void CloseOrderDb(double orderId, string closeType, BinanceOrder binanceOrder)
    {
        Order myOrder = _appDbContext.Order.Where(p => p.Id == orderId).Select(p => p).FirstOrDefault();
        if (!Global.onHold.ContainsKey(myOrder.Symbol)) Global.onHold.Add(myOrder.Symbol, true);

        myOrder.ClosePrice = TradeHelper.CalculateAvragePrice(binanceOrder);
        myOrder.QuantitySell = binanceOrder.fills.Sum(fill => Helper.ToDouble(fill.qty));
        myOrder.Profit = Math.Round((myOrder.ClosePrice - myOrder.OpenPrice) * myOrder.QuantitySell);
        myOrder.IsClosed = 1;
        myOrder.Type = closeType;
        myOrder.CloseDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
        _appDbContext.SaveChanges();
    }

    public void UpdateOrderDb(double id, BinanceOrder binanceOrder)
    {
        Order myOrder = _appDbContext.Order.Where(p => p.Id == id).FirstOrDefault();
        myOrder.OrderId = binanceOrder.orderId;
        myOrder.Status = binanceOrder.status;
        _appDbContext.Order.Add(myOrder);
        _appDbContext.SaveChanges();
    }

    #endregion
}
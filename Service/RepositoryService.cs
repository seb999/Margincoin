using System.Collections.Generic;
using MarginCoin.Class;
using MarginCoin.Model;
using MarginCoin.Service;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

public class RepositoryService : IRepositoryService
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

    #endregion


}
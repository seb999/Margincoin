using System.Collections.Generic;
using System.Timers;
using MarginCoin.MLClass;
using MarginCoin.Class;
using MarginCoin.Model;

namespace MarginCoin.Service
{

    public interface IRepositoryService
    {
        void SaveHighLow(List<Candle> symbolCandles, Order activeOrder);
        List<Order> GetActiveOrder();

        void UpdateTakeProfit(List<Candle> symbolCandle, Order activeOrder, double takeProfitPercentage);
    
        void UpdateStopLoss(List<Candle> symbolCandles, Order activeOrder);
    }
}
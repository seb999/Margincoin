using System.Collections.Generic;
using System.Timers;
using MarginCoin.MLClass;
using MarginCoin.Class;
using MarginCoin.Model;

namespace MarginCoin.Service
{

    public interface IOrderService
    {
        void SaveHighLow(List<Candle> symbolCandles, Order activeOrder);
        List<Order> GetActiveOrder();

        void UpdateTakeProfit(List<Candle> symbolCandle, Order activeOrder, double takeProfitPercentage);
    
        void UpdateStopLoss(List<Candle> symbolCandles, Order activeOrder);

        void SaveOrderDb(MarketStream symbolSpot, List<Candle> symbolCandle, BinanceOrder binanceOrder);

        public void CloseOrderDb(double orderId, string closeType, BinanceOrder binanceOrder);

        public void UpdateOrderDb(double id, BinanceOrder binanceOrder);
    }
}
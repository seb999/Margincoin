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

        public void CloseOrderDb(double orderId, BinanceOrder binanceOrder);

        public void UpdateStatusDb(double id, BinanceOrder binanceOrder);
        public void DeleteOrder(double id);
        public void UpdateTypeDb(double id, string closeType);
         public void UpdateBuyOrderDb(double id, BinanceOrder binanceOrder);
         public void UpdateSellOrderDb(double id, BinanceOrder binanceOrder);
        public void RecycleOrderDb(double id);
    }
}
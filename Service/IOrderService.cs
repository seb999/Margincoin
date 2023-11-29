using System.Collections.Generic;
using System.Timers;
using MarginCoin.MLClass;
using MarginCoin.Class;
using MarginCoin.Model;
using System.Threading.Tasks;

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
        public void DeleteOrderDb(double id);
        public void UpdateBuyOrderDb(double id, BinanceOrder binanceOrder);
        public void UpdateSellOrderDb(double id, BinanceOrder binanceOrder, string closeType);
        public void RecycleOrderDb(double id);
        public Task BuyLimit(MarketStream symbolSpot, List<Candle> symbolCandleList);
        public Task SellLimit(double id, List<MarketStream> marketStreamOnSpot, string closeType);
        public void SellMarket(double id, string closeType);
        public void BuyMarket(MarketStream symbolSpot, List<Candle> symbolCandleList);
    }
}
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
        public void SaveHighLow(List<Candle> symbolCandles, Order activeOrder);
        public List<Order> GetActiveOrder();
        public void UpdateTakeProfit(List<Candle> symbolCandle, Order activeOrder, double takeProfitPercentage);
        public void UpdateStopLoss(List<Candle> symbolCandles, Order activeOrder);
        public void SaveBuyOrderDb(MarketStream symbolSpot, List<Candle> symbolCandle, BinanceOrder binanceOrder);
        public void CloseOrderDb(Order dbOrder, BinanceOrder binanceOrder);
        public void DeleteOrderDb(double id);
        public void UpdateBuyOrderDb(Order dbOrder, BinanceOrder binanceOrder);
        public void UpdateSellOrderDb(Order dbOrder, BinanceOrder binanceOrder, string closeType);
        public void RecycleOrderDb(double id);
        public Task BuyLimit(MarketStream symbolSpot, List<Candle> symbolCandleList);
        public Task SellLimit(Order dbOrder, MarketStream symbolSpot, string closeType);
        public Task SellMarket(Order dbOrder, string closeType);
        public Task BuyMarket(MarketStream symbolSpot, List<Candle> symbolCandleList);
    }
}
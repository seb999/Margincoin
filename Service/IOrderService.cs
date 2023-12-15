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
        public void SaveHighLow(List<Candle> symbolCandles, Order activeOrder, ApplicationDbContext _appDbContext);
        public List<Order> GetActiveOrder(ApplicationDbContext _appDbContext);
        public void UpdateTakeProfit(List<Candle> symbolCandle, Order activeOrder, double takeProfitPercentage, ApplicationDbContext _appDbContext);
        public void UpdateStopLoss(List<Candle> symbolCandles, Order activeOrder, ApplicationDbContext _appDbContext);
        public void SaveBuyOrderDb(MarketStream symbolSpot, List<Candle> symbolCandle, BinanceOrder binanceOrder, ApplicationDbContext _appDbContext);
        public void CloseOrderDb(Order dbOrder, BinanceOrder binanceOrder, ApplicationDbContext _appDbContext);
        public void DeleteOrderDb(double id, ApplicationDbContext _appDbContext);
        public void UpdateBuyOrderDb(Order dbOrder, BinanceOrder binanceOrder, ApplicationDbContext _appDbContext);
        public void UpdateSellOrderDb(Order dbOrder, BinanceOrder binanceOrder, string closeType, ApplicationDbContext _appDbContext);
        public void RecycleOrderDb(double id, ApplicationDbContext _appDbContext);
        public Task BuyLimit(MarketStream symbolSpot, List<Candle> symbolCandleList, ApplicationDbContext _appDbContext);
        public Task SellLimit(Order dbOrder, List<MarketStream> marketStreamOnSpot, string closeType, ApplicationDbContext _appDbContext);
        public void SellMarket(Order dbOrder, string closeType, ApplicationDbContext _appDbContext);
        public void BuyMarket(MarketStream symbolSpot, List<Candle> symbolCandleList, ApplicationDbContext _appDbContext);
    }
}
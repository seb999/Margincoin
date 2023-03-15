using Binance.Spot;
using static MarginCoin.Service.WatchDog;

namespace MarginCoin.Service
{
    public interface IWebSocket
    {
        MarketDataWebSocket ws { get; set; }
        MarketDataWebSocket ws1 { get; set; }
    }
}

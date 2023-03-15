using System.Timers;
using Binance.Spot;

namespace MarginCoin.Service
{
    public class WebSocket : IWebSocket
    {
        public MarketDataWebSocket ws { get; set; }
        public MarketDataWebSocket ws1 { get; set; }

        public WebSocket()
        {
            
        }
    }
}
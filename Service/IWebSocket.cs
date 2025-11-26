using System.Collections.Generic;
using Binance.Spot;
using static MarginCoin.Service.WatchDog;

namespace MarginCoin.Service
{
    public interface IWebSocket
    {
        Dictionary<string, MarketDataWebSocket> SymbolWebSockets { get; }
        MarketDataWebSocket ws1 { get; set; }

        void AddSymbolWebSocket(string symbolName, MarketDataWebSocket webSocket);
        MarketDataWebSocket GetSymbolWebSocket(string symbolName);
        bool HasSymbolWebSocket(string symbolName);
        void RemoveSymbolWebSocket(string symbolName);
        void ClearSymbolWebSockets();
    }
}

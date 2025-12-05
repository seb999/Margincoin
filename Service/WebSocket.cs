using System.Collections.Generic;
using System.Timers;
using Binance.Spot;

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

    public class WebSocket : IWebSocket
    {
        public Dictionary<string, MarketDataWebSocket> SymbolWebSockets { get; } = new Dictionary<string, MarketDataWebSocket>();
        public MarketDataWebSocket ws1 { get; set; }

        public WebSocket()
        {

        }

        public void AddSymbolWebSocket(string symbolName, MarketDataWebSocket webSocket)
        {
            SymbolWebSockets[symbolName] = webSocket;
        }

        public MarketDataWebSocket GetSymbolWebSocket(string symbolName)
        {
            return SymbolWebSockets.TryGetValue(symbolName, out var ws) ? ws : null;
        }

        public bool HasSymbolWebSocket(string symbolName)
        {
            return SymbolWebSockets.ContainsKey(symbolName);
        }

        public void RemoveSymbolWebSocket(string symbolName)
        {
            SymbolWebSockets.Remove(symbolName);
        }

        public void ClearSymbolWebSockets()
        {
            SymbolWebSockets.Clear();
        }
    }
}
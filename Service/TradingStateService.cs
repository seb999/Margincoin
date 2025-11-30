using System.Collections.Generic;
using MarginCoin.Class;
using MarginCoin.Model;

namespace MarginCoin.Service
{
    /// <summary>
    /// Manages runtime trading state (replaces Global.cs static state).
    /// Registered as Singleton to maintain state across requests.
    /// </summary>
    public interface ITradingState
    {
        // Symbol lists
        List<Symbol> SymbolWeTrade { get; set; }
        List<Symbol> SymbolBaseList { get; set; }

        // Trading data
        List<List<Candle>> CandleMatrix { get; }
        List<MarketStream> MarketStreamOnSpot { get; set; }
        List<MarketStream> AllMarketData { get; set; }
        Dictionary<string, bool> OnHold { get; }

        // Flags
        bool IsProd { get; set; }
        bool IsTradingOpen { get; set; }
        bool IsMarketOrder { get; set; }
        bool SyncBinanceSymbol { get; set; }
        bool IsDbBusy { get; set; }
        bool TestBuyLimit { get; set; }

        // Methods
        void ClearCandleMatrix();
    }

    public class TradingStateService : ITradingState
    {
        private readonly object _candleLock = new object();

        public TradingStateService()
        {
            SymbolWeTrade = new List<Symbol>();
            SymbolBaseList = new List<Symbol>();
            CandleMatrix = new List<List<Candle>>();
            MarketStreamOnSpot = new List<MarketStream>();
            AllMarketData = new List<MarketStream>();
            OnHold = new Dictionary<string, bool>();
        }

        // Symbol lists
        public List<Symbol> SymbolWeTrade { get; set; }
        public List<Symbol> SymbolBaseList { get; set; }

        // Trading data
        public List<List<Candle>> CandleMatrix { get; private set; }
        public List<MarketStream> MarketStreamOnSpot { get; set; }
        public List<MarketStream> AllMarketData { get; set; }
        public Dictionary<string, bool> OnHold { get; private set; }

        // Flags
        public bool IsProd { get; set; } = false;
        public bool IsTradingOpen { get; set; } = false;
        public bool IsMarketOrder { get; set; } = false;
        public bool SyncBinanceSymbol { get; set; } = false;
        public bool IsDbBusy { get; set; } = false;
        public bool TestBuyLimit { get; set; } = false;

        public void ClearCandleMatrix()
        {
            lock (_candleLock)
            {
                CandleMatrix.Clear();
            }
        }
    }
}

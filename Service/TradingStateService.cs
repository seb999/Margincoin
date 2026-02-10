using System;
using System.Collections.Generic;
using System.Linq;
using MarginCoin.Class;
using MarginCoin.Model;
using Microsoft.Extensions.Logging;

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
        void CleanupOldData(int maxSymbols = 50, int maxCandlesPerSymbol = 500);
        void CleanupStaleOnHold(HashSet<string> activeSymbols);
    }

    public class TradingStateService : ITradingState
    {
        private readonly object _candleLock = new object();
        private DateTime _lastCleanup = DateTime.UtcNow;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(15);
        private readonly ILogger<TradingStateService> _logger;

        public TradingStateService(ILogger<TradingStateService> logger)
        {
            _logger = logger;
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

        public void CleanupOldData(int maxSymbols = 50, int maxCandlesPerSymbol = 500)
        {
            // Only cleanup every 15 minutes to avoid overhead
            if (DateTime.UtcNow - _lastCleanup < _cleanupInterval)
            {
                return;
            }

            lock (_candleLock)
            {
                var candleMatrixRemoved = 0;
                var candlesPerSymbolRemoved = 0;
                var marketDataRemoved = 0;
                var marketStreamRemoved = 0;

                // Limit CandleMatrix size - keep only recent symbols
                if (CandleMatrix.Count > maxSymbols)
                {
                    candleMatrixRemoved = CandleMatrix.Count - maxSymbols;
                    CandleMatrix.RemoveRange(0, candleMatrixRemoved);
                }

                // Limit candles per symbol - keep only recent candles
                foreach (var candles in CandleMatrix)
                {
                    if (candles.Count > maxCandlesPerSymbol)
                    {
                        var toRemove = candles.Count - maxCandlesPerSymbol;
                        candles.RemoveRange(0, toRemove);
                        candlesPerSymbolRemoved += toRemove;
                    }
                }

                // Clean old AllMarketData - keep only last 1000 entries
                if (AllMarketData.Count > 1000)
                {
                    marketDataRemoved = AllMarketData.Count - 500;
                    AllMarketData.RemoveRange(0, marketDataRemoved);
                }

                // Clean old MarketStreamOnSpot
                if (MarketStreamOnSpot.Count > 500)
                {
                    marketStreamRemoved = MarketStreamOnSpot.Count - 300;
                    MarketStreamOnSpot.RemoveRange(0, marketStreamRemoved);
                }

                _lastCleanup = DateTime.UtcNow;

                _logger.LogInformation(
                    "Memory cleanup completed. CandleMatrix: {CandleMatrixCount} symbols ({Removed1} removed), " +
                    "Total candles removed: {Removed2}, AllMarketData: {MarketDataCount} ({Removed3} removed), " +
                    "MarketStreamOnSpot: {StreamCount} ({Removed4} removed), OnHold: {OnHoldCount}",
                    CandleMatrix.Count, candleMatrixRemoved,
                    candlesPerSymbolRemoved, AllMarketData.Count, marketDataRemoved,
                    MarketStreamOnSpot.Count, marketStreamRemoved, OnHold.Count);
            }
        }

        public void CleanupStaleOnHold(HashSet<string> activeSymbols)
        {
            var staleKeys = OnHold.Keys.Where(k => !activeSymbols.Contains(k)).ToList();

            if (staleKeys.Count > 0)
            {
                foreach (var key in staleKeys)
                {
                    OnHold.Remove(key);
                }

                _logger.LogInformation(
                    "Cleaned {Count} stale OnHold entries: {Symbols}. Active symbols: {ActiveCount}",
                    staleKeys.Count, string.Join(", ", staleKeys), activeSymbols.Count);
            }
        }
    }
}

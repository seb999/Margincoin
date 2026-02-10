using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MarginCoin.Model;
using MarginCoin.Class;
using MarginCoin.Misc;
using MarginCoin.Service;
using MarginCoin.Configuration;
using Microsoft.Extensions.Options;
using System;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ActionController : ControllerBase
    {
        private readonly ITradingState _tradingState;
        private readonly ISymbolService _symbolService;
        private readonly TradingConfiguration _tradingConfig;

        public ActionController(
            ITradingState tradingState,
            ISymbolService symbolService,
            IOptions<TradingConfiguration> tradingConfig)
        {
            _tradingState = tradingState;
            _symbolService = symbolService;
            _tradingConfig = tradingConfig.Value;
        }

        [HttpGet("[action]/{symbol}")]
        public MacdSlope MacdSlope(string symbol)
        {
            var candleSymbol = _tradingState.CandleMatrix
                .FirstOrDefault(p => p.LastOrDefault()?.s == symbol);

            if (candleSymbol == null) return null;
            return TradeHelper.CalculateMacdSlope(candleSymbol.ToList(), _tradingConfig.Interval);
        }

        [HttpGet("[action]")]
        public void SyncBinanceSymbol()
        {
            _tradingState.SyncBinanceSymbol = true;
        }

        [HttpGet("[action]")]
        public List<Symbol> GetSymbolList() => _symbolService.GetTopSymbols(_tradingConfig.NumberOfSymbols);

        public List<Symbol> GetSymbolBaseList() => _symbolService.GetTopSymbols(_tradingConfig.NumberOfSymbols);

        [HttpGet("[action]")]
        public IEnumerable<object> TrendScores()
        {
            var scores = new Dictionary<string, int>();
            var matrix = _tradingState.CandleMatrix ?? new List<List<Candle>>();

            Console.WriteLine($"[TrendScores] CandleMatrix count: {matrix.Count}");

            foreach (var candleList in matrix)
            {
                var last = candleList?.LastOrDefault();
                if (last == null || string.IsNullOrEmpty(last.s))
                {
                    Console.WriteLine($"[TrendScores] Skipping candleList - last is null or empty symbol");
                    continue;
                }

                var baseSymbol = last.s.Replace("USDC", "").Replace("USDT", "");
                var score = TradeHelper.CalculateTrendScore(candleList.ToList(), _tradingConfig.UseWeightedTrendScore);
                scores[baseSymbol] = score;
                scores[last.s] = score;
                Console.WriteLine($"[TrendScores] {last.s} -> Score: {score}");
            }

            Console.WriteLine($"[TrendScores] Total scores calculated: {scores.Count}");

            var result = scores.Select(kv => new
            {
                symbol = kv.Key.Replace("USDC", "").Replace("USDT", ""),
                pair = kv.Key,
                score = kv.Value
            }).ToList();

            Console.WriteLine($"[TrendScores] Returning {result.Count} trend scores");
            return result;
        }

        [HttpGet("[action]")]
        public IEnumerable<object> SurgeScores()
        {
            var scores = new Dictionary<string, double>();
            var matrix = _tradingState.CandleMatrix ?? new List<List<Candle>>();
            var market = _tradingState.AllMarketData ?? new List<MarketStream>();

            foreach (var candleList in matrix)
            {
                var last = candleList?.LastOrDefault();
                if (last == null || string.IsNullOrEmpty(last.s)) continue;

                var marketData = market.FirstOrDefault(m => m.s == last.s);
                if (marketData == null) continue;

                var score = CalculateSurgeScore(marketData, candleList.ToList());

                // Skip invalid scores (NegativeInfinity, PositiveInfinity, NaN)
                if (double.IsInfinity(score) || double.IsNaN(score)) continue;

                var baseSymbol = last.s.Replace("USDC", "").Replace("USDT", "");
                scores[baseSymbol] = score;
                scores[last.s] = score;
            }

            return scores.Select(kv => new
            {
                symbol = kv.Key.Replace("USDC", "").Replace("USDT", ""),
                pair = kv.Key,
                score = kv.Value
            });
        }

        private double CalculateSurgeScore(MarketStream marketData, List<Candle> symbolCandles)
        {
            if (symbolCandles == null || symbolCandles.Count < 3)
                return double.NegativeInfinity;

            var last = symbolCandles[^1];
            var prev = symbolCandles[^2];
            var prev2 = symbolCandles[^3];

            // Keep RSI within configured bounds to avoid overbought/oversold churn
            if (last.Rsi < _tradingConfig.MinRSI || last.Rsi > _tradingConfig.MaxRSI)
                return double.NegativeInfinity;

            var trendScore = TradeHelper.CalculateTrendScore(symbolCandles, _tradingConfig.UseWeightedTrendScore);
            var recentMove = prev.c > 0 ? (last.c - prev.c) / prev.c * 100 : 0;
            var prevMove = prev2.c > 0 ? (prev.c - prev2.c) / prev2.c * 100 : 0;
            var acceleration = recentMove - prevMove;
            var macdSlopeBoost = Math.Max(0, last.MacdSlope * 100);
            var volumeSpike = prev.v > 0 ? Math.Min(last.v / prev.v, 5) : 1;

            // Weighted composite; tuned to highlight fast upside moves with confirmation
            var score = trendScore * 0.6
                        + recentMove * 0.5
                        + acceleration * 0.3
                        + macdSlopeBoost
                        + (volumeSpike - 1)
                        + marketData.P / 2.0;

            return score;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MarginCoin.Model;
using MarginCoin.Class;
using MarginCoin.Misc;
using MarginCoin.Service;
using MarginCoin.Configuration;
using Microsoft.Extensions.Options;

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
        public List<Symbol> GetSymbolList() => _symbolService.GetTradingSymbols();

        public List<Symbol> GetSymbolBaseList() => _symbolService.GetBaseSymbols();

        [HttpGet("[action]")]
        public IEnumerable<object> TrendScores()
        {
            var scores = new Dictionary<string, int>();
            var matrix = _tradingState.CandleMatrix ?? new List<List<Candle>>();

            foreach (var candleList in matrix)
            {
                var last = candleList?.LastOrDefault();
                if (last == null || string.IsNullOrEmpty(last.s)) continue;

                var baseSymbol = last.s.Replace("USDC", "").Replace("USDT", "");
                var score = TradeHelper.CalculateTrendScore(candleList.ToList(), _tradingConfig.UseWeightedTrendScore);
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
    }
}

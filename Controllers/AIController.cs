using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MarginCoin.Class;
using MarginCoin.MLClass;
using MarginCoin.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AIController : ControllerBase
    {
        private readonly LSTMPredictionService _predictionService;
        private readonly ITradingState _tradingState;
        private readonly ILogger<AIController> _logger;

        public AIController(
            LSTMPredictionService predictionService,
            ITradingState tradingState,
            ILogger<AIController> logger)
        {
            _predictionService = predictionService;
            _tradingState = tradingState;
            _logger = logger;
        }

        /// <summary>
        /// Health check for the Python AI service.
        /// </summary>
        [HttpGet("[action]")]
        public async Task<IActionResult> Health()
        {
            var healthy = await _predictionService.IsHealthyAsync();
            return healthy
                ? Ok(new { status = "ok" })
                : StatusCode(503, new { status = "unhealthy" });
        }

        /// <summary>
        /// Returns the latest AI predictions for symbols currently tracked in memory.
        /// </summary>
        [HttpGet("[action]")]
        public async Task<IEnumerable<AIPredictionDto>> Signals()
        {
            var candleMatrix = _tradingState.CandleMatrix ?? new List<List<Candle>>();
            var tradingPairs = new HashSet<string>(
                (_tradingState.SymbolWeTrade ?? new List<MarginCoin.Model.Symbol>())
                    .Where(s => !string.IsNullOrWhiteSpace(s.SymbolName))
                    .Select(s => s.SymbolName),
                StringComparer.OrdinalIgnoreCase);

            if (!candleMatrix.Any())
            {
                return Enumerable.Empty<AIPredictionDto>();
            }

            var predictionTasks = candleMatrix
                .Where(c =>
                    c != null &&
                    c.Count >= 50 &&
                    c.LastOrDefault() != null &&
                    tradingPairs.Contains(c.Last().s))
                .Select(async candles =>
                {
                    var symbol = candles.Last().s;
                    try
                    {
                        var prediction = await _predictionService.PredictAsync(symbol, candles);
                        return MapToDto(symbol, prediction);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to fetch AI prediction for {Symbol}", symbol);
                        return MapToDto(symbol, null);
                    }
                });

            var results = await Task.WhenAll(predictionTasks);

            // De-duplicate by pair to avoid returning multiple entries for the same symbol
            return results
                .Where(r => r != null)
                .GroupBy(r => r.Pair)
                .Select(g => g.OrderByDescending(r => r.Timestamp ?? DateTime.MinValue).First());
        }

        private static AIPredictionDto MapToDto(string symbol, MLPrediction prediction)
        {
            var baseSymbol = symbol?
                .Replace("USDC", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("USDT", string.Empty, StringComparison.OrdinalIgnoreCase);

            return new AIPredictionDto
            {
                Symbol = baseSymbol,
                Pair = symbol,
                Prediction = prediction?.PredictedLabel,
                Confidence = prediction?.Confidence ?? 0,
                ExpectedReturn = prediction?.ExpectedReturn ?? 0,
                UpProbability = prediction?.Score != null && prediction.Score.Length > 2 ? prediction.Score[2] : (float?)null,
                SidewaysProbability = prediction?.Score != null && prediction.Score.Length > 1 ? prediction.Score[1] : (float?)null,
                DownProbability = prediction?.Score != null && prediction.Score.Length > 0 ? prediction.Score[0] : (float?)null,
                TrendScore = prediction?.TrendScore,
                Timestamp = prediction?.Timestamp
            };
        }
    }
}

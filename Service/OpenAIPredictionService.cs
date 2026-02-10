using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MarginCoin.Class;
using Microsoft.Extensions.Logging;

namespace MarginCoin.Service
{
    /// <summary>
    /// Service to get AI-powered trading signals from OpenAI via Python ML API
    /// </summary>
    public class OpenAIPredictionService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OpenAIPredictionService> _logger;
        private readonly string _apiBaseUrl;

        public OpenAIPredictionService(
            ILogger<OpenAIPredictionService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30); // OpenAI can take longer

            _apiBaseUrl = Environment.GetEnvironmentVariable("ML_API_URL") ?? "http://localhost:8000";
            _logger.LogInformation($"OpenAI Prediction Service initialized with API: {_apiBaseUrl}");
        }

        /// <summary>
        /// Get AI trading signal for a symbol based on current indicators
        /// </summary>
        /// <param name="symbol">Trading symbol</param>
        /// <param name="currentCandle">Current candle with indicators</param>
        /// <param name="previousCandle">Previous candle for trend analysis (optional)</param>
        /// <returns>OpenAI analysis result</returns>
        public async Task<OpenAIAnalysisResult> GetTradingSignalAsync(
            string symbol,
            Candle currentCandle,
            Candle previousCandle = null)
        {
            try
            {
                var request = new OpenAIIndicatorRequest
                {
                    Symbol = symbol,
                    Indicators = new Dictionary<string, double>
                    {
                        ["close"] = currentCandle.c,
                        ["rsi"] = currentCandle.Rsi,
                        ["macd"] = currentCandle.Macd,
                        ["macd_signal"] = currentCandle.MacdSign,
                        ["macd_hist"] = currentCandle.MacdHist,
                        ["ema50"] = currentCandle.Ema,
                        ["volume"] = currentCandle.v,
                        ["stoch_k"] = currentCandle.StochSlowK,
                        ["stoch_d"] = currentCandle.StochSlowD
                    }
                };

                // Add previous candle data if available
                if (previousCandle != null)
                {
                    request.PreviousIndicators = new Dictionary<string, double>
                    {
                        ["close"] = previousCandle.c,
                        ["rsi"] = previousCandle.Rsi,
                        ["macd_hist"] = previousCandle.MacdHist
                    };
                }

                // Make API call
                var response = await _httpClient.PostAsJsonAsync(
                    $"{_apiBaseUrl}/predict/openai",
                    request);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"OpenAI API error for {symbol}: {response.StatusCode} - {error}");
                    return null;
                }

                var result = await response.Content.ReadFromJsonAsync<OpenAIAnalysisResult>();

                if (result != null)
                {
                    _logger.LogInformation(
                        $"OpenAI Signal for {symbol}: {result.Signal} " +
                        $"(Score: {result.TradingScore}/10, Confidence: {result.Confidence:P})");
                    _logger.LogDebug($"Reasoning: {result.Reasoning}");
                }

                return result;
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning($"OpenAI API timeout for {symbol}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting OpenAI signal for {symbol}");
                return null;
            }
        }

        /// <summary>
        /// Get AI trading signals for multiple symbols in parallel
        /// </summary>
        public async Task<Dictionary<string, OpenAIAnalysisResult>> GetBulkTradingSignalsAsync(
            Dictionary<string, (Candle current, Candle previous)> symbolData)
        {
            var tasks = symbolData.Select(kvp =>
                GetTradingSignalAsync(kvp.Key, kvp.Value.current, kvp.Value.previous)
                    .ContinueWith(t => new { Symbol = kvp.Key, Result = t.Result }));

            var results = await Task.WhenAll(tasks);

            return results
                .Where(r => r.Result != null)
                .ToDictionary(r => r.Symbol, r => r.Result);
        }

        /// <summary>
        /// Check if the signal is strong enough to act on
        /// </summary>
        public static bool IsStrongSignal(OpenAIAnalysisResult analysis, double minConfidence = 0.7, int minScore = 6)
        {
            if (analysis == null) return false;

            return analysis.Signal == "BUY" &&
                   analysis.Confidence >= minConfidence &&
                   analysis.TradingScore >= minScore;
        }

        /// <summary>
        /// Check if it's a strong sell signal
        /// </summary>
        public static bool IsStrongSellSignal(OpenAIAnalysisResult analysis, double minConfidence = 0.7, int minScore = -6)
        {
            if (analysis == null) return false;

            return analysis.Signal == "SELL" &&
                   analysis.Confidence >= minConfidence &&
                   analysis.TradingScore <= minScore;
        }
    }

    #region DTOs for OpenAI API Communication

    public class OpenAIIndicatorRequest
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }

        [JsonPropertyName("indicators")]
        public Dictionary<string, double> Indicators { get; set; }

        [JsonPropertyName("previous_indicators")]
        public Dictionary<string, double> PreviousIndicators { get; set; }
    }

    public class OpenAIAnalysisResult
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }

        [JsonPropertyName("signal")]
        public string Signal { get; set; } // "BUY", "SELL", "HOLD"

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("trading_score")]
        public int TradingScore { get; set; } // -10 to +10

        [JsonPropertyName("reasoning")]
        public string Reasoning { get; set; }

        [JsonPropertyName("risk_level")]
        public string RiskLevel { get; set; } // "LOW", "MEDIUM", "HIGH"

        [JsonPropertyName("key_factors")]
        public List<string> KeyFactors { get; set; }
    }

    #endregion
}

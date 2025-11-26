using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MarginCoin.Class;
using MarginCoin.MLClass;
using Microsoft.Extensions.Logging;
using static MarginCoin.Service.MLService;

namespace MarginCoin.Service
{
    /// <summary>
    /// Service to communicate with Python LSTM/Transformer ML prediction API
    /// Replaces image-based ML with time-series prediction
    /// </summary>
    public class LSTMPredictionService : IMLService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<LSTMPredictionService> _logger;
        private readonly string _apiBaseUrl;

        // Cache for predictions to avoid overwhelming the API
        private readonly Dictionary<string, (MLPrediction prediction, DateTime timestamp)> _predictionCache;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromSeconds(30);

        public List<MLPrediction> MLPredList { get; set; }

        public LSTMPredictionService(ILogger<LSTMPredictionService> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);

            _apiBaseUrl = Environment.GetEnvironmentVariable("ML_API_URL") ?? "http://localhost:8000";
            _predictionCache = new Dictionary<string, (MLPrediction, DateTime)>();
            MLPredList = new List<MLPrediction>();

            _logger.LogInformation($"LSTM Prediction Service initialized with API: {_apiBaseUrl}");
        }

        /// <summary>
        /// Check if the ML API is healthy and ready
        /// </summary>
        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/health");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"ML API health check failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get prediction for a symbol based on recent candles
        /// </summary>
        /// <param name="symbol">Trading symbol (e.g., "BTCUSDT")</param>
        /// <param name="candles">List of recent candles (minimum 50)</param>
        /// <returns>ML prediction with confidence scores</returns>
        public async Task<MLPrediction> PredictAsync(string symbol, List<Candle> candles)
        {
            // Check cache first
            if (_predictionCache.TryGetValue(symbol, out var cached))
            {
                if (DateTime.Now - cached.timestamp < _cacheExpiry)
                {
                    _logger.LogDebug($"Returning cached prediction for {symbol}");
                    return cached.prediction;
                }
            }

            try
            {
                // Ensure we have enough candles
                if (candles.Count < 50)
                {
                    _logger.LogWarning($"Insufficient candles for {symbol}: {candles.Count} (need 50+)");
                    return CreateFallbackPrediction(symbol);
                }

                // Take last 50 candles for prediction
                var recentCandles = candles.TakeLast(50).ToList();

                // Prepare request
                var request = new PredictionRequest
                {
                    Symbol = symbol,
                    Candles = recentCandles.Select(c => new CandleDto
                    {
                        Open = c.o,
                        High = c.h,
                        Low = c.l,
                        Close = c.c,
                        Volume = c.v,
                        Rsi = c.Rsi,
                        Macd = c.Macd,
                        MacdSign = c.MacdSign,
                        MacdHist = c.MacdHist,
                        Ema = c.Ema,
                        StochSlowK = c.StochSlowK,
                        StochSlowD = c.StochSlowD
                    }).ToList()
                };

                // Make API call
                var response = await _httpClient.PostAsJsonAsync($"{_apiBaseUrl}/predict", request);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"ML API error for {symbol}: {response.StatusCode} - {error}");
                    return CreateFallbackPrediction(symbol);
                }

                var result = await response.Content.ReadFromJsonAsync<PredictionResponse>();

                if (result == null)
                {
                    _logger.LogError($"Failed to deserialize prediction response for {symbol}");
                    return CreateFallbackPrediction(symbol);
                }

                // Convert to MLPrediction format
                var prediction = new MLPrediction
                {
                    Symbol = symbol,
                    PredictedLabel = result.Prediction.ToLower(),
                    Score = new[]
                    {
                        (float)result.Probabilities.Down,
                        (float)result.Probabilities.Sideways,
                        (float)result.Probabilities.Up
                    },
                    Confidence = result.Confidence,
                    ExpectedReturn = result.ExpectedReturn,
                    TrendScore = result.TrendScore,
                    Timestamp = DateTime.Now
                };

                // Update cache
                _predictionCache[symbol] = (prediction, DateTime.Now);

                // Update prediction list
                UpdatePredictionList(prediction);

                _logger.LogInformation(
                    $"Prediction for {symbol}: {prediction.PredictedLabel.ToUpper()} " +
                    $"(confidence: {prediction.Confidence:P}, expected return: {prediction.ExpectedReturn:P2})"
                );

                return prediction;
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning($"ML API timeout for {symbol}");
                return CreateFallbackPrediction(symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting prediction for {symbol}");
                return CreateFallbackPrediction(symbol);
            }
        }

        /// <summary>
        /// Update predictions for multiple symbols in parallel
        /// </summary>
        public async Task UpdatePredictionsAsync(List<(string symbol, List<Candle> candles)> symbolData)
        {
            var tasks = symbolData.Select(data => PredictAsync(data.symbol, data.candles));
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Create fallback prediction when ML API is unavailable
        /// Returns neutral prediction
        /// </summary>
        private MLPrediction CreateFallbackPrediction(string symbol)
        {
            return new MLPrediction
            {
                Symbol = symbol,
                PredictedLabel = "sideways",
                Score = new[] { 0.33f, 0.34f, 0.33f },  // Neutral probabilities
                Confidence = 0.34,
                ExpectedReturn = 0,
                TrendScore = null,
                Timestamp = DateTime.Now
            };
        }

        /// <summary>
        /// Update the global prediction list (maintains compatibility with existing code)
        /// </summary>
        private void UpdatePredictionList(MLPrediction prediction)
        {
            var existing = MLPredList.FirstOrDefault(p => p.Symbol == prediction.Symbol);
            if (existing != null)
            {
                MLPredList.Remove(existing);
            }
            MLPredList.Add(prediction);

            // Keep only recent predictions (last 100)
            if (MLPredList.Count > 100)
            {
                MLPredList = MLPredList.OrderByDescending(p => p.Timestamp).Take(100).ToList();
            }
        }

        // Methods to maintain interface compatibility
        public void InitML(TimeElapseDelegate dddd) { }
        public void StopML() { }
        public void UpdateML() { }
        public void CleanImageFolder() { }
    }

    #region DTOs for API Communication

    public class PredictionRequest
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }

        [JsonPropertyName("candles")]
        public List<CandleDto> Candles { get; set; }
    }

    public class CandleDto
    {
        [JsonPropertyName("open")]
        public double Open { get; set; }

        [JsonPropertyName("high")]
        public double High { get; set; }

        [JsonPropertyName("low")]
        public double Low { get; set; }

        [JsonPropertyName("close")]
        public double Close { get; set; }

        [JsonPropertyName("volume")]
        public double Volume { get; set; }

        [JsonPropertyName("rsi")]
        public double Rsi { get; set; }

        [JsonPropertyName("macd")]
        public double Macd { get; set; }

        [JsonPropertyName("macdSign")]
        public double MacdSign { get; set; }

        [JsonPropertyName("macdHist")]
        public double MacdHist { get; set; }

        [JsonPropertyName("ema")]
        public double Ema { get; set; }

        [JsonPropertyName("stochSlowK")]
        public double StochSlowK { get; set; }

        [JsonPropertyName("stochSlowD")]
        public double StochSlowD { get; set; }
    }

    public class PredictionResponse
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }

        [JsonPropertyName("prediction")]
        public string Prediction { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("probabilities")]
        public ProbabilityScores Probabilities { get; set; }

        [JsonPropertyName("expected_return")]
        public double ExpectedReturn { get; set; }

        [JsonPropertyName("trend_score")]
        public int? TrendScore { get; set; }

        [JsonPropertyName("attention_summary")]
        public Dictionary<string, double> AttentionSummary { get; set; }
    }

    public class ProbabilityScores
    {
        [JsonPropertyName("down")]
        public double Down { get; set; }

        [JsonPropertyName("sideways")]
        public double Sideways { get; set; }

        [JsonPropertyName("up")]
        public double Up { get; set; }
    }

    #endregion
}

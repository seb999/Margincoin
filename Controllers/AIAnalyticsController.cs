using System;
using System.Collections.Generic;
using System.Linq;
using MarginCoin.Misc;
using MarginCoin.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AIAnalyticsController : ControllerBase
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<AIAnalyticsController> _logger;
        private static readonly string PredictionUpLabel = MyEnum.PredictionDirection.Up.ToLabel();
        private static readonly string PredictionDownLabel = MyEnum.PredictionDirection.Down.ToLabel();
        private static readonly string PredictionSidewayLabel = MyEnum.PredictionDirection.Sideway.ToLabel();

        public AIAnalyticsController(ApplicationDbContext dbContext, ILogger<AIAnalyticsController> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Get comprehensive AI performance metrics
        /// </summary>
        [HttpGet("[action]")]
        public IActionResult PerformanceMetrics([FromQuery] int days = 30)
        {
            var cutoffDate = DateTime.Now.AddDays(-days);

            var closedOrders = _dbContext.Order
                .Where(o => o.IsClosed == 1
                    && !string.IsNullOrEmpty(o.CloseDate)
                    && !string.IsNullOrEmpty(o.AIPrediction))
                .AsEnumerable()
                .Where(o => ParseDate(o.CloseDate) >= cutoffDate)
                .ToList();

            if (!closedOrders.Any())
            {
                return Ok(new { message = "No closed orders with AI predictions found", totalOrders = 0 });
            }

            var metrics = new
            {
                // Overall Statistics
                totalOrders = closedOrders.Count,
                profitableOrders = closedOrders.Count(o => o.Profit > 0),
                losingOrders = closedOrders.Count(o => o.Profit < 0),
                winRate = closedOrders.Count > 0 ? (double)closedOrders.Count(o => o.Profit > 0) / closedOrders.Count : 0,

                // Entry Prediction Analysis
                entryPredictions = new
                {
                    // When AI predicted UP at entry, how many were profitable?
                    upPredictionAccuracy = CalculateAccuracy(closedOrders, PredictionUpLabel),
                    upPredictionCount = closedOrders.Count(o => o.AIPrediction == PredictionUpLabel),
                    upPredictionProfitable = closedOrders.Count(o => o.AIPrediction == PredictionUpLabel && o.Profit > 0),

                    // When AI predicted DOWN at entry (shouldn't happen, but track it)
                    downPredictionAccuracy = CalculateAccuracy(closedOrders, PredictionDownLabel),
                    downPredictionCount = closedOrders.Count(o => o.AIPrediction == PredictionDownLabel),

                    // Sideways predictions
                    sidewaysPredictionAccuracy = CalculateAccuracy(closedOrders, PredictionSidewayLabel),
                    sidewaysPredictionCount = closedOrders.Count(o => o.AIPrediction == PredictionSidewayLabel),
                },

                // Exit Prediction Analysis
                exitPredictions = new
                {
                    // When AI predicted DOWN at exit, was it correct?
                    downExitAccuracy = CalculateExitAccuracy(closedOrders, PredictionDownLabel),
                    downExitCount = closedOrders.Count(o => !string.IsNullOrEmpty(o.ExitAIPrediction) && o.ExitAIPrediction == PredictionDownLabel),

                    // Other exit predictions
                    upExitCount = closedOrders.Count(o => !string.IsNullOrEmpty(o.ExitAIPrediction) && o.ExitAIPrediction == PredictionUpLabel),
                    sidewaysExitCount = closedOrders.Count(o => !string.IsNullOrEmpty(o.ExitAIPrediction) && o.ExitAIPrediction == PredictionSidewayLabel),
                },

                // Confidence Analysis
                confidenceAnalysis = new
                {
                    highConfidenceEntry = AnalyzeByConfidence(closedOrders, 0.7, 1.0, true),
                    mediumConfidenceEntry = AnalyzeByConfidence(closedOrders, 0.5, 0.7, true),
                    lowConfidenceEntry = AnalyzeByConfidence(closedOrders, 0.0, 0.5, true),

                    highConfidenceExit = AnalyzeByConfidence(closedOrders, 0.7, 1.0, false),
                    mediumConfidenceExit = AnalyzeByConfidence(closedOrders, 0.5, 0.7, false),
                    lowConfidenceExit = AnalyzeByConfidence(closedOrders, 0.0, 0.5, false),
                },

                // Profit Analysis by AI Prediction
                profitByPrediction = closedOrders
                    .GroupBy(o => o.AIPrediction)
                    .Select(g => new
                    {
                        prediction = g.Key,
                        totalProfit = g.Sum(o => o.Profit),
                        avgProfit = g.Average(o => o.Profit),
                        count = g.Count(),
                        winRate = g.Count(o => o.Profit > 0) / (double)g.Count()
                    })
                    .OrderByDescending(x => x.totalProfit)
                    .ToList(),

                // AI Prediction Changes (Entry vs Exit)
                predictionChanges = AnalyzePredictionChanges(closedOrders),

                // Time-based Performance
                performanceByDay = closedOrders
                    .GroupBy(o => ParseDate(o.CloseDate).Date)
                    .Select(g => new
                    {
                        date = g.Key.ToString("yyyy-MM-dd"),
                        totalOrders = g.Count(),
                        profitableOrders = g.Count(o => o.Profit > 0),
                        totalProfit = g.Sum(o => o.Profit),
                        avgEntryConfidence = g.Average(o => o.AIScore),
                        avgExitConfidence = g.Where(o => o.ExitAIScore > 0).Average(o => o.ExitAIScore)
                    })
                    .OrderBy(x => x.date)
                    .ToList()
            };

            return Ok(metrics);
        }

        /// <summary>
        /// Get detailed AI prediction accuracy breakdown
        /// </summary>
        [HttpGet("[action]")]
        public IActionResult PredictionAccuracy([FromQuery] int days = 30)
        {
            var cutoffDate = DateTime.Now.AddDays(-days);

            var orders = _dbContext.Order
                .Where(o => o.IsClosed == 1 && !string.IsNullOrEmpty(o.AIPrediction))
                .AsEnumerable()
                .Where(o => ParseDate(o.CloseDate) >= cutoffDate)
                .ToList();

            var analysis = new
            {
                // Entry Accuracy: Did "UP" prediction lead to profit?
                entryAccuracy = new
                {
                    upPrediction = new
                    {
                        total = orders.Count(o => o.AIPrediction == PredictionUpLabel),
                        correct = orders.Count(o => o.AIPrediction == PredictionUpLabel && o.Profit > 0),
                        incorrect = orders.Count(o => o.AIPrediction == PredictionUpLabel && o.Profit <= 0),
                        accuracy = CalculateAccuracy(orders, PredictionUpLabel),
                        avgProfitWhenCorrect = orders.Where(o => o.AIPrediction == PredictionUpLabel && o.Profit > 0).Any()
                            ? orders.Where(o => o.AIPrediction == PredictionUpLabel && o.Profit > 0).Average(o => o.Profit)
                            : 0,
                        avgLossWhenIncorrect = orders.Where(o => o.AIPrediction == PredictionUpLabel && o.Profit <= 0).Any()
                            ? orders.Where(o => o.AIPrediction == PredictionUpLabel && o.Profit <= 0).Average(o => o.Profit)
                            : 0
                    }
                },

                // Exit Accuracy: Did "DOWN" prediction at exit mean we avoided further loss?
                exitAccuracy = new
                {
                    downPrediction = new
                    {
                        total = orders.Count(o => o.ExitAIPrediction == PredictionDownLabel),
                        // Good exits: predicted down and we had profit OR predicted down and stopped further loss
                        goodExits = orders.Count(o => o.ExitAIPrediction == PredictionDownLabel && o.Profit > 0),
                        badExits = orders.Count(o => o.ExitAIPrediction == PredictionDownLabel && o.Profit <= 0),
                        avgProfitOnExit = orders.Where(o => o.ExitAIPrediction == PredictionDownLabel).Any()
                            ? orders.Where(o => o.ExitAIPrediction == PredictionDownLabel).Average(o => o.Profit)
                            : 0
                    }
                },

                // Confidence vs Accuracy
                confidenceCorrelation = orders
                    .GroupBy(o => Math.Round(o.AIScore, 1))
                    .Select(g => new
                    {
                        confidenceLevel = g.Key,
                        count = g.Count(),
                        accuracy = g.Count() > 0 ? (double)g.Count(o => o.Profit > 0) / g.Count() : 0,
                        avgProfit = g.Average(o => o.Profit)
                    })
                    .OrderBy(x => x.confidenceLevel)
                    .ToList(),

                // Symbol-specific Performance
                symbolPerformance = orders
                    .GroupBy(o => o.Symbol)
                    .Select(g => new
                    {
                        symbol = g.Key,
                        totalOrders = g.Count(),
                        accuracy = g.Count() > 0 ? (double)g.Count(o => o.Profit > 0) / g.Count() : 0,
                        totalProfit = g.Sum(o => o.Profit),
                        avgConfidence = g.Average(o => o.AIScore)
                    })
                    .OrderByDescending(x => x.totalOrders)
                    .Take(10)
                    .ToList()
            };

            return Ok(analysis);
        }

        /// <summary>
        /// Compare AI predictions vs actual outcomes
        /// </summary>
        [HttpGet("[action]")]
        public IActionResult PredictionVsActual([FromQuery] int limit = 50)
        {
            var recentOrders = _dbContext.Order
                .Where(o => o.IsClosed == 1 && !string.IsNullOrEmpty(o.AIPrediction))
                .OrderByDescending(o => o.Id)
                .Take(limit)
                .AsEnumerable()
                .Select(o => new
                {
                    orderId = o.Id,
                    symbol = o.Symbol,
                    openDate = o.OpenDate,
                    closeDate = o.CloseDate,

                    // Entry
                    entryPrediction = o.AIPrediction,
                    entryConfidence = Math.Round(o.AIScore, 3),

                    // Exit
                    exitPrediction = o.ExitAIPrediction,
                    exitConfidence = Math.Round(o.ExitAIScore, 3),

                    // Actual Result
                    profit = Math.Round(o.Profit, 2),
                    profitPercent = o.OpenPrice > 0 ? Math.Round((o.ClosePrice - o.OpenPrice) / o.OpenPrice * 100, 2) : 0,

                    // Analysis
                    entryWasCorrect = (o.AIPrediction == PredictionUpLabel && o.Profit > 0) || (o.AIPrediction == PredictionDownLabel && o.Profit < 0),
                    exitWasCorrect = string.IsNullOrEmpty(o.ExitAIPrediction) ? (bool?)null :
                                     (o.ExitAIPrediction == PredictionDownLabel && o.Profit > 0), // Exited before further loss

                    closeReason = o.Type
                })
                .ToList();

            return Ok(recentOrders);
        }

        /// <summary>
        /// Get AI model recommendations based on performance
        /// </summary>
        [HttpGet("[action]")]
        public IActionResult Recommendations([FromQuery] int days = 30)
        {
            var cutoffDate = DateTime.Now.AddDays(-days);

            var orders = _dbContext.Order
                .Where(o => o.IsClosed == 1 && !string.IsNullOrEmpty(o.AIPrediction))
                .AsEnumerable()
                .Where(o => ParseDate(o.CloseDate) >= cutoffDate)
                .ToList();

            if (!orders.Any())
            {
                return Ok(new { message = "Not enough data for recommendations" });
            }

            var overallAccuracy = CalculateAccuracy(orders, PredictionUpLabel);
            var highConfResult = AnalyzeByConfidence(orders, 0.7, 1.0, true);
            var highConfAccuracy = (double)((dynamic)highConfResult).accuracy;
            var avgProfit = orders.Average(o => o.Profit);

            var recommendations = new List<string>();

            // Recommendation 1: Overall performance
            if (overallAccuracy >= 0.6)
            {
                recommendations.Add($"✅ Model shows good entry accuracy ({overallAccuracy:P1}). Consider enabling AI-based entries.");
            }
            else if (overallAccuracy >= 0.5)
            {
                recommendations.Add($"⚠️ Model shows moderate accuracy ({overallAccuracy:P1}). Use with caution or only for high-confidence predictions.");
            }
            else
            {
                recommendations.Add($"❌ Model shows low accuracy ({overallAccuracy:P1}). NOT recommended for automated trading yet. More training needed.");
            }

            // Recommendation 2: Confidence threshold
            if (highConfAccuracy > overallAccuracy + 0.1)
            {
                recommendations.Add($"💡 High-confidence predictions (>70%) perform significantly better ({highConfAccuracy:P1}). Set MinAIScore to 0.7 in config.");
            }

            // Recommendation 3: Exit signals
            var exitOrders = orders.Where(o => !string.IsNullOrEmpty(o.ExitAIPrediction) && o.ExitAIPrediction == PredictionDownLabel).ToList();
            if (exitOrders.Any())
            {
                var exitEffectiveness = (double)exitOrders.Count(o => o.Profit > 0) / exitOrders.Count;
                if (exitEffectiveness >= 0.7)
                {
                    recommendations.Add($"✅ AI exit signals are effective ({exitEffectiveness:P1} saved profit). Consider enabling AIVetoConfidence.");
                }
            }

            // Recommendation 4: Profitability
            if (avgProfit > 0)
            {
                recommendations.Add($"📈 Overall profitable with AI ({avgProfit:F2} avg profit per trade).");
            }
            else
            {
                recommendations.Add($"📉 Currently unprofitable ({avgProfit:F2} avg loss per trade). Review strategy parameters.");
            }

            // Recommendation 5: Sample size
            if (orders.Count < 30)
            {
                recommendations.Add($"⚠️ Limited data ({orders.Count} trades). Collect more data (recommend 100+ trades) before full automation.");
            }

            return Ok(new
            {
                analysisDate = DateTime.Now,
                daysPeriod = days,
                totalTrades = orders.Count,
                overallAccuracy = overallAccuracy,
                averageProfit = avgProfit,
                recommendations = recommendations,

                suggestedConfig = new
                {
                    enableAI = overallAccuracy >= 0.6,
                    minAIScore = highConfAccuracy > overallAccuracy + 0.1 ? 0.7 : 0.6,
                    aiVetoConfidence = exitOrders.Any() && (double)exitOrders.Count(o => o.Profit > 0) / exitOrders.Count >= 0.7 ? 0.85 : 0.97
                }
            });
        }

        #region Helper Methods

        private double CalculateAccuracy(List<Order> orders, string prediction)
        {
            var filtered = orders.Where(o => o.AIPrediction == prediction).ToList();
            if (!filtered.Any()) return 0;

            var correct = filtered.Count(o =>
                (prediction == PredictionUpLabel && o.Profit > 0) ||
                (prediction == PredictionDownLabel && o.Profit < 0) ||
                (prediction == PredictionSidewayLabel && Math.Abs(o.Profit) < 10)); // Small profit/loss for sideways

            return (double)correct / filtered.Count;
        }

        private double CalculateExitAccuracy(List<Order> orders, string exitPrediction)
        {
            var filtered = orders.Where(o => !string.IsNullOrEmpty(o.ExitAIPrediction) && o.ExitAIPrediction == exitPrediction).ToList();
            if (!filtered.Any()) return 0;

            // Exit was good if we had profit when exiting on DOWN signal
            var goodExits = filtered.Count(o => o.Profit > 0);

            return (double)goodExits / filtered.Count;
        }

        private object AnalyzeByConfidence(List<Order> orders, double minConf, double maxConf, bool isEntry)
        {
            var filtered = isEntry
                ? orders.Where(o => o.AIScore >= minConf && o.AIScore < maxConf).ToList()
                : orders.Where(o => o.ExitAIScore >= minConf && o.ExitAIScore < maxConf).ToList();

            if (!filtered.Any())
                return new { count = 0, accuracy = 0.0, avgProfit = 0.0 };

            return new
            {
                count = filtered.Count,
                accuracy = (double)filtered.Count(o => o.Profit > 0) / filtered.Count,
                avgProfit = filtered.Average(o => o.Profit),
                totalProfit = filtered.Sum(o => o.Profit)
            };
        }

        private object AnalyzePredictionChanges(List<Order> orders)
        {
            var withExitPrediction = orders.Where(o => !string.IsNullOrEmpty(o.ExitAIPrediction)).ToList();

            if (!withExitPrediction.Any())
                return new { totalWithExitData = 0 };

            return new
            {
                totalWithExitData = withExitPrediction.Count,

                // UP → DOWN (reversal detected)
                upToDown = new
                {
                    count = withExitPrediction.Count(o => o.AIPrediction == PredictionUpLabel && o.ExitAIPrediction == PredictionDownLabel),
                    avgProfit = withExitPrediction.Where(o => o.AIPrediction == PredictionUpLabel && o.ExitAIPrediction == PredictionDownLabel).Any()
                        ? withExitPrediction.Where(o => o.AIPrediction == PredictionUpLabel && o.ExitAIPrediction == PredictionDownLabel).Average(o => o.Profit)
                        : 0
                },

                // UP → UP (continued confidence)
                upToUp = new
                {
                    count = withExitPrediction.Count(o => o.AIPrediction == PredictionUpLabel && o.ExitAIPrediction == PredictionUpLabel),
                    avgProfit = withExitPrediction.Where(o => o.AIPrediction == PredictionUpLabel && o.ExitAIPrediction == PredictionUpLabel).Any()
                        ? withExitPrediction.Where(o => o.AIPrediction == PredictionUpLabel && o.ExitAIPrediction == PredictionUpLabel).Average(o => o.Profit)
                        : 0
                },

                // UP → SIDEWAYS
                upToSideways = new
                {
                    count = withExitPrediction.Count(o => o.AIPrediction == PredictionUpLabel && o.ExitAIPrediction == PredictionSidewayLabel),
                    avgProfit = withExitPrediction.Where(o => o.AIPrediction == PredictionUpLabel && o.ExitAIPrediction == PredictionSidewayLabel).Any()
                        ? withExitPrediction.Where(o => o.AIPrediction == PredictionUpLabel && o.ExitAIPrediction == PredictionSidewayLabel).Average(o => o.Profit)
                        : 0
                }
            };
        }

        private DateTime ParseDate(string dateStr)
        {
            if (string.IsNullOrEmpty(dateStr)) return DateTime.MinValue;

            try
            {
                return DateTime.ParseExact(dateStr, "dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        #endregion
    }
}

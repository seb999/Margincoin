using System;
using System.Linq;
using System.Threading.Tasks;
using MarginCoin.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MarginCoin.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PerformanceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<PerformanceController> _logger;

        public PerformanceController(ApplicationDbContext context, ILogger<PerformanceController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetPerformance()
        {
            try
            {
                // Get all filled sell orders (completed trades)
                var completedTrades = await _context.Order
                    .Where(o => o.Status.ToUpper() == "FILLED" && o.Side == "SELL" && o.Profit != 0)
                    .ToListAsync();

                if (!completedTrades.Any())
                {
                    return Ok(new
                    {
                        totalTrades = 0,
                        winningTrades = 0,
                        losingTrades = 0,
                        totalGains = 0.0,
                        totalLosses = 0.0,
                        netProfit = 0.0,
                        bestTrade = 0.0,
                        worstTrade = 0.0,
                        trades = new object[0]
                    });
                }

                var winningTrades = completedTrades.Where(t => t.Profit > 0).ToList();
                var losingTrades = completedTrades.Where(t => t.Profit <= 0).ToList();

                double totalGains = winningTrades.Sum(t => t.Profit);
                double totalLosses = losingTrades.Sum(t => t.Profit);
                double netProfit = totalGains + totalLosses;

                double bestTrade = completedTrades.Any() ? completedTrades.Max(t => t.Profit) : 0;
                double worstTrade = completedTrades.Any() ? completedTrades.Min(t => t.Profit) : 0;

                // Get individual trades for chart (ordered by close date)
                var trades = completedTrades
                    .OrderBy(t => t.CloseDate)
                    .Select(t => new
                    {
                        symbol = t.Symbol,
                        profit = t.Profit,
                        closeDate = t.CloseDate,
                        closePrice = t.ClosePrice,
                        maxPotentialProfit = t.Side == "SELL" ? (t.HighPrice - t.OpenPrice) * t.QuantityBuy : t.Profit
                    })
                    .ToList();

                var performance = new
                {
                    totalTrades = completedTrades.Count,
                    winningTrades = winningTrades.Count,
                    losingTrades = losingTrades.Count,
                    totalGains = totalGains,
                    totalLosses = totalLosses,
                    netProfit = netProfit,
                    bestTrade = bestTrade,
                    worstTrade = worstTrade,
                    trades = trades
                };

                return Ok(performance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating performance metrics");
                return StatusCode(500, new { error = "Failed to calculate performance metrics" });
            }
        }
    }
}

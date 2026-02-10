using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MarginCoin.Class;
using MarginCoin.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace MarginCoin.Service
{
    public interface ICandleDataService
    {
        /// <summary>
        /// Save or update candle data in the database
        /// </summary>
        Task SaveCandleAsync(string symbol, string interval, StreamData streamData);

        /// <summary>
        /// Get the latest N candles for a symbol
        /// </summary>
        Task<List<Candle>> GetCandlesAsync(string symbol, string interval, int limit = 50);

        /// <summary>
        /// Get count of closed candles for a symbol
        /// </summary>
        Task<int> GetCandleCountAsync(string symbol, string interval);

        /// <summary>
        /// Check if we have enough candles to start trading (minimum 50)
        /// </summary>
        Task<bool> HasEnoughCandlesAsync(string symbol, string interval, int minRequired = 50);

        /// <summary>
        /// Delete old candles beyond retention limit (keep last N candles)
        /// </summary>
        Task CleanupOldCandlesAsync(string symbol, string interval, int keep = 200);

        /// <summary>
        /// Get all symbols that have enough candles for trading
        /// </summary>
        Task<List<string>> GetSymbolsReadyForTradingAsync(string interval, int minRequired = 50);

        /// <summary>
        /// Check if candle data is fresh (within specified time window)
        /// </summary>
        Task<bool> IsDataFreshAsync(string interval, TimeSpan maxAge);

        /// <summary>
        /// Get the timestamp of the latest candle for an interval
        /// </summary>
        Task<DateTime?> GetLatestCandleTimeAsync(string interval);

        /// <summary>
        /// Delete all candles for a specific interval
        /// </summary>
        Task DeleteIntervalDataAsync(string interval);
    }

    public class CandleDataService : ICandleDataService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CandleDataService> _logger;

        public CandleDataService(
            IServiceScopeFactory scopeFactory,
            ILogger<CandleDataService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task SaveCandleAsync(string symbol, string interval, StreamData streamData)
        {
            if (streamData?.k == null) return;

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                var openTime = (long)streamData.k.t;

                // Check if candle already exists
                var existingCandle = await dbContext.CandleHistory
                    .FirstOrDefaultAsync(c =>
                        c.Symbol == symbol &&
                        c.Interval == interval &&
                        c.OpenTime == openTime);

                if (existingCandle != null)
                {
                    // Update existing candle
                    existingCandle.Close = streamData.k.c;
                    existingCandle.High = streamData.k.h;
                    existingCandle.Low = streamData.k.l;
                    existingCandle.Volume = streamData.k.v;
                    existingCandle.IsClosed = streamData.k.x;
                    existingCandle.CloseTime = (long)streamData.k.T;
                    existingCandle.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    // Create new candle
                    var candle = new CandleHistory
                    {
                        Symbol = symbol,
                        Interval = interval,
                        OpenTime = openTime,
                        CloseTime = (long)streamData.k.T,
                        Open = streamData.k.o,
                        High = streamData.k.h,
                        Low = streamData.k.l,
                        Close = streamData.k.c,
                        Volume = streamData.k.v,
                        IsClosed = streamData.k.x,
                        UpdatedAt = DateTime.UtcNow
                    };

                    dbContext.CandleHistory.Add(candle);
                }

                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save candle for {Symbol} at {Interval}", symbol, interval);
                throw;
            }
        }

        public async Task<List<Candle>> GetCandlesAsync(string symbol, string interval, int limit = 50)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                var candleHistories = await dbContext.CandleHistory
                    .Where(c => c.Symbol == symbol && c.Interval == interval && c.IsClosed)
                    .OrderBy(c => c.OpenTime)
                    .Take(limit)
                    .ToListAsync();

                // Convert CandleHistory to Candle
                return candleHistories.Select(ch => new Candle
                {
                    s = ch.Symbol,
                    T = ch.OpenTime,
                    t = ch.CloseTime,
                    o = ch.Open,
                    h = ch.High,
                    l = ch.Low,
                    c = ch.Close,
                    v = ch.Volume,
                    x = ch.IsClosed,
                    P = ch.PriceChangePercent,
                    Rsi = ch.RSI ?? 0,
                    Macd = ch.MACD ?? 0,
                    MacdSign = ch.MACDSignal ?? 0,
                    MacdHist = ch.MACDHist ?? 0,
                    MacdSlope = ch.MACDSlope ?? 0,
                    Ema = ch.EMA ?? 0,
                    StochSlowK = ch.StochSlowK ?? 0,
                    StochSlowD = ch.StochSlowD ?? 0,
                    ATR = ch.ATR ?? 0
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get candles for {Symbol} at {Interval}", symbol, interval);
                return new List<Candle>();
            }
        }

        public async Task<int> GetCandleCountAsync(string symbol, string interval)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                return await dbContext.CandleHistory
                    .CountAsync(c => c.Symbol == symbol && c.Interval == interval && c.IsClosed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get candle count for {Symbol} at {Interval}", symbol, interval);
                return 0;
            }
        }

        public async Task<bool> HasEnoughCandlesAsync(string symbol, string interval, int minRequired = 50)
        {
            var count = await GetCandleCountAsync(symbol, interval);
            return count >= minRequired;
        }

        public async Task CleanupOldCandlesAsync(string symbol, string interval, int keep = 200)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                var totalCount = await dbContext.CandleHistory
                    .CountAsync(c => c.Symbol == symbol && c.Interval == interval && c.IsClosed);

                if (totalCount <= keep) return;

                // Get the OpenTime threshold (keep only the last 'keep' candles)
                var thresholdTime = await dbContext.CandleHistory
                    .Where(c => c.Symbol == symbol && c.Interval == interval && c.IsClosed)
                    .OrderByDescending(c => c.OpenTime)
                    .Skip(keep)
                    .Select(c => c.OpenTime)
                    .FirstOrDefaultAsync();

                // Delete candles older than threshold
                var oldCandles = dbContext.CandleHistory
                    .Where(c => c.Symbol == symbol && c.Interval == interval && c.OpenTime < thresholdTime);

                dbContext.CandleHistory.RemoveRange(oldCandles);
                await dbContext.SaveChangesAsync();

                _logger.LogInformation("Cleaned up old candles for {Symbol}, kept last {Keep}", symbol, keep);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup old candles for {Symbol} at {Interval}", symbol, interval);
            }
        }

        public async Task<List<string>> GetSymbolsReadyForTradingAsync(string interval, int minRequired = 50)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                var symbols = await dbContext.CandleHistory
                    .Where(c => c.Interval == interval && c.IsClosed)
                    .GroupBy(c => c.Symbol)
                    .Select(g => new { Symbol = g.Key, Count = g.Count() })
                    .Where(x => x.Count >= minRequired)
                    .Select(x => x.Symbol)
                    .ToListAsync();

                return symbols;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get symbols ready for trading at {Interval}", interval);
                return new List<string>();
            }
        }

        public async Task<bool> IsDataFreshAsync(string interval, TimeSpan maxAge)
        {
            var latestTime = await GetLatestCandleTimeAsync(interval);

            if (!latestTime.HasValue)
            {
                return false; // No data at all
            }

            var age = DateTime.UtcNow - latestTime.Value;
            return age <= maxAge;
        }

        public async Task<DateTime?> GetLatestCandleTimeAsync(string interval)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                var latestCandle = await dbContext.CandleHistory
                    .Where(c => c.Interval == interval && c.IsClosed)
                    .OrderByDescending(c => c.CloseTime)
                    .FirstOrDefaultAsync();

                if (latestCandle == null)
                {
                    return null;
                }

                // CloseTime is Unix timestamp in milliseconds
                return DateTimeOffset.FromUnixTimeMilliseconds(latestCandle.CloseTime).UtcDateTime;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get latest candle time for {Interval}", interval);
                return null;
            }
        }

        public async Task DeleteIntervalDataAsync(string interval)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                var toDelete = dbContext.CandleHistory.Where(c => c.Interval == interval);
                dbContext.CandleHistory.RemoveRange(toDelete);
                await dbContext.SaveChangesAsync();

                _logger.LogInformation("Deleted all candles for interval {Interval}", interval);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete interval data for {Interval}", interval);
            }
        }
    }
}

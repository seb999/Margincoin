using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MarginCoin.Configuration;
using MarginCoin.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarginCoin.Service
{
    public interface ITradingSettingsService
    {
        /// <summary>
        /// Get static configuration from appsettings.json (read-only)
        /// </summary>
        TradingConfiguration GetStaticConfig();

        /// <summary>
        /// Get runtime settings from database (can be modified at runtime)
        /// </summary>
        Task<RuntimeTradingSettings> GetRuntimeSettingsAsync();

        /// <summary>
        /// Update runtime settings in database
        /// </summary>
        Task<RuntimeTradingSettings> UpdateRuntimeSettingsAsync(RuntimeTradingSettingsDto dto);
    }

    public class RuntimeTradingSettingsDto
    {
        public int? MaxOpenTrades { get; set; }
        public double? QuoteOrderQty { get; set; }
        public double? StopLossPercentage { get; set; }
        public double? TakeProfitPercentage { get; set; }
        public int? TimeBasedKillMinutes { get; set; }
        public bool? EnableAggressiveReplacement { get; set; }
        public double? SurgeScoreThreshold { get; set; }
        public double? ReplacementScoreGap { get; set; }
        public int? ReplacementCooldownSeconds { get; set; }
        public int? MaxReplacementsPerHour { get; set; }
        public int? MaxCandidateDepth { get; set; }
        public double? WeakTrendStopLossPercentage { get; set; }
        public bool? EnableDynamicStopLoss { get; set; }
        public double? TrailingStopPercentage { get; set; }
        public bool? EnableMLPredictions { get; set; }
        public bool? EnableOpenAISignals { get; set; }
    }

    public class TradingSettingsService : ITradingSettingsService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly TradingConfiguration _staticConfig;
        private readonly ILogger<TradingSettingsService> _logger;
        private RuntimeTradingSettings _runtimeCache;
        private DateTime _lastCacheUpdate = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(5);

        public TradingSettingsService(
            ApplicationDbContext dbContext,
            IOptions<TradingConfiguration> config,
            ILogger<TradingSettingsService> logger)
        {
            _dbContext = dbContext;
            _staticConfig = config.Value;
            _logger = logger;
        }

        public TradingConfiguration GetStaticConfig()
        {
            return _staticConfig;
        }

        public async Task<RuntimeTradingSettings> GetRuntimeSettingsAsync()
        {
            // Return cached version if still valid
            if (_runtimeCache != null && DateTime.UtcNow - _lastCacheUpdate < _cacheExpiration)
            {
                return _runtimeCache;
            }

            await EnsureTableAsync();
            await SeedDefaultsIfEmptyAsync();

            var settings = await _dbContext.RuntimeSettings.AsNoTracking().ToListAsync();
            var runtime = new RuntimeTradingSettings();

            foreach (var setting in settings)
            {
                ApplySettingToRuntime(runtime, setting);
            }

            _runtimeCache = runtime;
            _lastCacheUpdate = DateTime.UtcNow;

            return runtime;
        }

        public async Task<RuntimeTradingSettings> UpdateRuntimeSettingsAsync(RuntimeTradingSettingsDto dto)
        {
            await EnsureTableAsync();
            await SeedDefaultsIfEmptyAsync();

            var updates = new Dictionary<string, string>();

            if (dto.MaxOpenTrades.HasValue)
                updates[nameof(RuntimeTradingSettings.MaxOpenTrades)] = dto.MaxOpenTrades.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.QuoteOrderQty.HasValue)
                updates[nameof(RuntimeTradingSettings.QuoteOrderQty)] = dto.QuoteOrderQty.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.StopLossPercentage.HasValue)
                updates[nameof(RuntimeTradingSettings.StopLossPercentage)] = dto.StopLossPercentage.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.TakeProfitPercentage.HasValue)
                updates[nameof(RuntimeTradingSettings.TakeProfitPercentage)] = dto.TakeProfitPercentage.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.TimeBasedKillMinutes.HasValue)
                updates[nameof(RuntimeTradingSettings.TimeBasedKillMinutes)] = dto.TimeBasedKillMinutes.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.EnableAggressiveReplacement.HasValue)
                updates[nameof(RuntimeTradingSettings.EnableAggressiveReplacement)] = dto.EnableAggressiveReplacement.Value.ToString();
            if (dto.SurgeScoreThreshold.HasValue)
                updates[nameof(RuntimeTradingSettings.SurgeScoreThreshold)] = dto.SurgeScoreThreshold.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.ReplacementScoreGap.HasValue)
                updates[nameof(RuntimeTradingSettings.ReplacementScoreGap)] = dto.ReplacementScoreGap.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.ReplacementCooldownSeconds.HasValue)
                updates[nameof(RuntimeTradingSettings.ReplacementCooldownSeconds)] = dto.ReplacementCooldownSeconds.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.MaxReplacementsPerHour.HasValue)
                updates[nameof(RuntimeTradingSettings.MaxReplacementsPerHour)] = dto.MaxReplacementsPerHour.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.MaxCandidateDepth.HasValue)
                updates[nameof(RuntimeTradingSettings.MaxCandidateDepth)] = dto.MaxCandidateDepth.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.WeakTrendStopLossPercentage.HasValue)
                updates[nameof(RuntimeTradingSettings.WeakTrendStopLossPercentage)] = dto.WeakTrendStopLossPercentage.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.EnableDynamicStopLoss.HasValue)
                updates[nameof(RuntimeTradingSettings.EnableDynamicStopLoss)] = dto.EnableDynamicStopLoss.Value.ToString();
            if (dto.TrailingStopPercentage.HasValue)
                updates[nameof(RuntimeTradingSettings.TrailingStopPercentage)] = dto.TrailingStopPercentage.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.EnableMLPredictions.HasValue)
                updates[nameof(RuntimeTradingSettings.EnableMLPredictions)] = dto.EnableMLPredictions.Value.ToString();
            if (dto.EnableOpenAISignals.HasValue)
                updates[nameof(RuntimeTradingSettings.EnableOpenAISignals)] = dto.EnableOpenAISignals.Value.ToString();

            foreach (var kv in updates)
            {
                var existing = await _dbContext.RuntimeSettings.FirstOrDefaultAsync(s => s.Key == kv.Key);
                if (existing == null)
                {
                    _dbContext.RuntimeSettings.Add(new RuntimeSetting { Key = kv.Key, Value = kv.Value });
                }
                else
                {
                    existing.Value = kv.Value;
                    _dbContext.RuntimeSettings.Update(existing);
                }
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Runtime trading settings updated: {Keys}", string.Join(", ", updates.Keys));

            // Invalidate cache
            _runtimeCache = null;

            return await GetRuntimeSettingsAsync();
        }

        private async Task EnsureTableAsync()
        {
            try
            {
                await _dbContext.Database.ExecuteSqlRawAsync(
                    "CREATE TABLE IF NOT EXISTS RuntimeSettings (Key TEXT PRIMARY KEY, Value TEXT)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure RuntimeSettings table exists");
            }
        }

        private void ApplySettingToRuntime(RuntimeTradingSettings runtime, RuntimeSetting setting)
        {
            try
            {
                switch (setting.Key)
                {
                    case nameof(RuntimeTradingSettings.MaxOpenTrades):
                        runtime.MaxOpenTrades = int.Parse(setting.Value, CultureInfo.InvariantCulture);
                        break;
                    case nameof(RuntimeTradingSettings.QuoteOrderQty):
                        runtime.QuoteOrderQty = double.Parse(setting.Value, CultureInfo.InvariantCulture);
                        break;
                    case nameof(RuntimeTradingSettings.StopLossPercentage):
                        runtime.StopLossPercentage = double.Parse(setting.Value, CultureInfo.InvariantCulture);
                        break;
                    case nameof(RuntimeTradingSettings.TakeProfitPercentage):
                        runtime.TakeProfitPercentage = double.Parse(setting.Value, CultureInfo.InvariantCulture);
                        break;
                    case nameof(RuntimeTradingSettings.TimeBasedKillMinutes):
                        runtime.TimeBasedKillMinutes = int.Parse(setting.Value, CultureInfo.InvariantCulture);
                        break;
                    case nameof(RuntimeTradingSettings.EnableAggressiveReplacement):
                        runtime.EnableAggressiveReplacement = bool.Parse(setting.Value);
                        break;
                    case nameof(RuntimeTradingSettings.SurgeScoreThreshold):
                        runtime.SurgeScoreThreshold = double.Parse(setting.Value, CultureInfo.InvariantCulture);
                        break;
                    case nameof(RuntimeTradingSettings.ReplacementScoreGap):
                        runtime.ReplacementScoreGap = double.Parse(setting.Value, CultureInfo.InvariantCulture);
                        break;
                    case nameof(RuntimeTradingSettings.ReplacementCooldownSeconds):
                        runtime.ReplacementCooldownSeconds = int.Parse(setting.Value, CultureInfo.InvariantCulture);
                        break;
                    case nameof(RuntimeTradingSettings.MaxReplacementsPerHour):
                        runtime.MaxReplacementsPerHour = int.Parse(setting.Value, CultureInfo.InvariantCulture);
                        break;
                    case nameof(RuntimeTradingSettings.MaxCandidateDepth):
                        runtime.MaxCandidateDepth = int.Parse(setting.Value, CultureInfo.InvariantCulture);
                        break;
                    case nameof(RuntimeTradingSettings.WeakTrendStopLossPercentage):
                        runtime.WeakTrendStopLossPercentage = double.Parse(setting.Value, CultureInfo.InvariantCulture);
                        break;
                    case nameof(RuntimeTradingSettings.EnableDynamicStopLoss):
                        runtime.EnableDynamicStopLoss = bool.Parse(setting.Value);
                        break;
                    case nameof(RuntimeTradingSettings.TrailingStopPercentage):
                        runtime.TrailingStopPercentage = double.Parse(setting.Value, CultureInfo.InvariantCulture);
                        break;
                    case nameof(RuntimeTradingSettings.EnableMLPredictions):
                        runtime.EnableMLPredictions = bool.Parse(setting.Value);
                        break;
                    case nameof(RuntimeTradingSettings.EnableOpenAISignals):
                        runtime.EnableOpenAISignals = bool.Parse(setting.Value);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply runtime setting {Key} with value {Value}", setting.Key, setting.Value);
            }
        }

        private async Task SeedDefaultsIfEmptyAsync()
        {
            try
            {
                if (!await _dbContext.RuntimeSettings.AnyAsync())
                {
                    var defaults = new RuntimeTradingSettings();
                    var seedData = new Dictionary<string, string>
                    {
                        { nameof(RuntimeTradingSettings.MaxOpenTrades), defaults.MaxOpenTrades.ToString(CultureInfo.InvariantCulture) },
                        { nameof(RuntimeTradingSettings.QuoteOrderQty), defaults.QuoteOrderQty.ToString(CultureInfo.InvariantCulture) },
                        { nameof(RuntimeTradingSettings.StopLossPercentage), defaults.StopLossPercentage.ToString(CultureInfo.InvariantCulture) },
                        { nameof(RuntimeTradingSettings.TakeProfitPercentage), defaults.TakeProfitPercentage.ToString(CultureInfo.InvariantCulture) },
                        { nameof(RuntimeTradingSettings.TimeBasedKillMinutes), defaults.TimeBasedKillMinutes.ToString(CultureInfo.InvariantCulture) },
                        { nameof(RuntimeTradingSettings.EnableAggressiveReplacement), defaults.EnableAggressiveReplacement.ToString() },
                        { nameof(RuntimeTradingSettings.SurgeScoreThreshold), defaults.SurgeScoreThreshold.ToString(CultureInfo.InvariantCulture) },
                        { nameof(RuntimeTradingSettings.ReplacementScoreGap), defaults.ReplacementScoreGap.ToString(CultureInfo.InvariantCulture) },
                        { nameof(RuntimeTradingSettings.ReplacementCooldownSeconds), defaults.ReplacementCooldownSeconds.ToString(CultureInfo.InvariantCulture) },
                        { nameof(RuntimeTradingSettings.MaxReplacementsPerHour), defaults.MaxReplacementsPerHour.ToString(CultureInfo.InvariantCulture) },
                        { nameof(RuntimeTradingSettings.MaxCandidateDepth), defaults.MaxCandidateDepth.ToString(CultureInfo.InvariantCulture) },
                        { nameof(RuntimeTradingSettings.WeakTrendStopLossPercentage), defaults.WeakTrendStopLossPercentage.ToString(CultureInfo.InvariantCulture) },
                        { nameof(RuntimeTradingSettings.EnableDynamicStopLoss), defaults.EnableDynamicStopLoss.ToString() },
                        { nameof(RuntimeTradingSettings.TrailingStopPercentage), defaults.TrailingStopPercentage.ToString(CultureInfo.InvariantCulture) },
                        { nameof(RuntimeTradingSettings.EnableMLPredictions), defaults.EnableMLPredictions.ToString() },
                        { nameof(RuntimeTradingSettings.EnableOpenAISignals), defaults.EnableOpenAISignals.ToString() }
                    };

                    foreach (var kv in seedData)
                    {
                        _dbContext.RuntimeSettings.Add(new RuntimeSetting { Key = kv.Key, Value = kv.Value });
                    }

                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("Seeded default runtime settings");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed default runtime settings");
            }
        }
    }
}

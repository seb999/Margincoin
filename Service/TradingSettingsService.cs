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
        Task<TradingConfiguration> GetAsync();
        Task<TradingConfiguration> UpdateAsync(TradingSettingsDto dto);
        Task ApplyOverridesAsync();
    }

    public class TradingSettingsDto
    {
        public string Interval { get; set; }
        public int? MaxOpenTrades { get; set; }
        public int? NumberOfSymbols { get; set; }
        public double? QuoteOrderQty { get; set; }
        public double? StopLossPercentage { get; set; }
        public double? TakeProfitPercentage { get; set; }
        public double? OrderOffset { get; set; }
        public double? MinPercentageUp { get; set; }
        public double? MinRSI { get; set; }
        public double? MaxRSI { get; set; }
        public int? TimeBasedKillMinutes { get; set; }
    }

    public class TradingSettingsService : ITradingSettingsService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly TradingConfiguration _config;
        private readonly ILogger<TradingSettingsService> _logger;

        public TradingSettingsService(
            ApplicationDbContext dbContext,
            IOptions<TradingConfiguration> config,
            ILogger<TradingSettingsService> logger)
        {
            _dbContext = dbContext;
            _config = config.Value;
            _logger = logger;
        }

        public async Task ApplyOverridesAsync()
        {
            try
            {
                await EnsureTableAsync();
                await SeedIfEmptyAsync();
                var overrides = await _dbContext.Settings.AsNoTracking().ToListAsync();
                ApplyToConfig(overrides);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply settings overrides; using defaults");
            }
        }

        public async Task<TradingConfiguration> GetAsync()
        {
            await EnsureTableAsync();
            await SeedIfEmptyAsync();
            await ApplyOverridesAsync();
            return _config;
        }

        public async Task<TradingConfiguration> UpdateAsync(TradingSettingsDto dto)
        {
            await EnsureTableAsync();
            await SeedIfEmptyAsync();

            var updates = new Dictionary<string, string>();

            if (!string.IsNullOrWhiteSpace(dto.Interval)) updates["Interval"] = dto.Interval;
            if (dto.MaxOpenTrades.HasValue) updates["MaxOpenTrades"] = dto.MaxOpenTrades.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.NumberOfSymbols.HasValue) updates["NumberOfSymbols"] = dto.NumberOfSymbols.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.QuoteOrderQty.HasValue) updates["QuoteOrderQty"] = dto.QuoteOrderQty.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.StopLossPercentage.HasValue) updates["StopLossPercentage"] = dto.StopLossPercentage.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.TakeProfitPercentage.HasValue) updates["TakeProfitPercentage"] = dto.TakeProfitPercentage.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.OrderOffset.HasValue) updates["OrderOffset"] = dto.OrderOffset.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.MinPercentageUp.HasValue) updates["MinPercentageUp"] = dto.MinPercentageUp.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.MinRSI.HasValue) updates["MinRSI"] = dto.MinRSI.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.MaxRSI.HasValue) updates["MaxRSI"] = dto.MaxRSI.Value.ToString(CultureInfo.InvariantCulture);
            if (dto.TimeBasedKillMinutes.HasValue) updates["TimeBasedKillMinutes"] = dto.TimeBasedKillMinutes.Value.ToString(CultureInfo.InvariantCulture);

            foreach (var kv in updates)
            {
                var existing = await _dbContext.Settings.FirstOrDefaultAsync(s => s.Key == kv.Key);
                if (existing == null)
                {
                    _dbContext.Settings.Add(new Setting { Key = kv.Key, Value = kv.Value });
                }
                else
                {
                    existing.Value = kv.Value;
                    _dbContext.Settings.Update(existing);
                }
            }

            await _dbContext.SaveChangesAsync();

            ApplyToConfig(updates.Select(u => new Setting { Key = u.Key, Value = u.Value }));
            _logger.LogInformation("Trading settings updated at runtime");
            return _config;
        }

        private async Task EnsureTableAsync()
        {
            try
            {
                await _dbContext.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure Settings table exists");
            }
        }

        private void ApplyToConfig(IEnumerable<Setting> overrides)
        {
            foreach (var ov in overrides)
            {
                try
                {
                    switch (ov.Key)
                    {
                        case nameof(TradingConfiguration.Interval):
                            _config.Interval = ov.Value;
                            break;
                        case nameof(TradingConfiguration.MaxOpenTrades):
                            _config.MaxOpenTrades = int.Parse(ov.Value);
                            break;
                        case nameof(TradingConfiguration.NumberOfSymbols):
                            _config.NumberOfSymbols = int.Parse(ov.Value);
                            break;
                        case nameof(TradingConfiguration.QuoteOrderQty):
                            _config.QuoteOrderQty = double.Parse(ov.Value, CultureInfo.InvariantCulture);
                            break;
                        case nameof(TradingConfiguration.StopLossPercentage):
                            _config.StopLossPercentage = double.Parse(ov.Value, CultureInfo.InvariantCulture);
                            break;
                        case nameof(TradingConfiguration.TakeProfitPercentage):
                            _config.TakeProfitPercentage = double.Parse(ov.Value, CultureInfo.InvariantCulture);
                            break;
                        case nameof(TradingConfiguration.OrderOffset):
                            _config.OrderOffset = double.Parse(ov.Value, CultureInfo.InvariantCulture);
                            break;
                        case nameof(TradingConfiguration.MinPercentageUp):
                            _config.MinPercentageUp = double.Parse(ov.Value, CultureInfo.InvariantCulture);
                            break;
                        case nameof(TradingConfiguration.MinRSI):
                            _config.MinRSI = double.Parse(ov.Value, CultureInfo.InvariantCulture);
                            break;
                        case nameof(TradingConfiguration.MaxRSI):
                            _config.MaxRSI = double.Parse(ov.Value, CultureInfo.InvariantCulture);
                            break;
                        case nameof(TradingConfiguration.TimeBasedKillMinutes):
                            _config.TimeBasedKillMinutes = int.Parse(ov.Value);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to apply setting {Key} with value {Value}", ov.Key, ov.Value);
                }
            }
        }

        private async Task SeedIfEmptyAsync()
        {
            try
            {
                if (!await _dbContext.Settings.AnyAsync())
                {
                    var defaults = new Dictionary<string, string>
                    {
                        { nameof(TradingConfiguration.Interval), _config.Interval },
                        { nameof(TradingConfiguration.MaxOpenTrades), _config.MaxOpenTrades.ToString() },
                        { nameof(TradingConfiguration.NumberOfSymbols), _config.NumberOfSymbols.ToString() },
                        { nameof(TradingConfiguration.QuoteOrderQty), _config.QuoteOrderQty.ToString(CultureInfo.InvariantCulture) },
                        { nameof(TradingConfiguration.StopLossPercentage), _config.StopLossPercentage.ToString(CultureInfo.InvariantCulture) },
                        { nameof(TradingConfiguration.TakeProfitPercentage), _config.TakeProfitPercentage.ToString(CultureInfo.InvariantCulture) },
                        { nameof(TradingConfiguration.OrderOffset), _config.OrderOffset.ToString(CultureInfo.InvariantCulture) },
                        { nameof(TradingConfiguration.MinPercentageUp), _config.MinPercentageUp.ToString(CultureInfo.InvariantCulture) },
                        { nameof(TradingConfiguration.MinRSI), _config.MinRSI.ToString(CultureInfo.InvariantCulture) },
                        { nameof(TradingConfiguration.MaxRSI), _config.MaxRSI.ToString(CultureInfo.InvariantCulture) },
                        { nameof(TradingConfiguration.TimeBasedKillMinutes), _config.TimeBasedKillMinutes.ToString() },
                    };

                    foreach (var kv in defaults)
                    {
                        _dbContext.Settings.Add(new Setting { Key = kv.Key, Value = kv.Value });
                    }

                    await _dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed default settings");
            }
        }
    }
}

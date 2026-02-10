using System.Threading.Tasks;
using MarginCoin.Configuration;
using MarginCoin.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SettingsController : ControllerBase
    {
        private readonly ITradingSettingsService _settingsService;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(ITradingSettingsService settingsService, ILogger<SettingsController> logger)
        {
            _settingsService = settingsService;
            _logger = logger;
        }

        /// <summary>
        /// Get static configuration from appsettings.json
        /// </summary>
        [HttpGet("static")]
        public ActionResult<TradingConfiguration> GetStatic()
        {
            try
            {
                var config = _settingsService.GetStaticConfig();
                return Ok(config);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to load static settings");
                return StatusCode(500, "Failed to load static settings");
            }
        }

        /// <summary>
        /// Get runtime settings from database
        /// </summary>
        [HttpGet("runtime")]
        public async Task<ActionResult<RuntimeTradingSettings>> GetRuntime()
        {
            try
            {
                var settings = await _settingsService.GetRuntimeSettingsAsync();
                return Ok(settings);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to load runtime settings");
                return StatusCode(500, "Failed to load runtime settings");
            }
        }

        /// <summary>
        /// Get combined settings (static + runtime) for backward compatibility
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<object>> Get()
        {
            try
            {
                var staticConfig = _settingsService.GetStaticConfig();
                var runtime = await _settingsService.GetRuntimeSettingsAsync();

                return Ok(new
                {
                    Static = staticConfig,
                    Runtime = runtime
                });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to load trading settings");
                return StatusCode(500, "Failed to load settings");
            }
        }

        /// <summary>
        /// Update runtime settings only
        /// </summary>
        [HttpPut("runtime")]
        public async Task<ActionResult<RuntimeTradingSettings>> UpdateRuntime([FromBody] RuntimeTradingSettingsDto dto)
        {
            if (dto == null) return BadRequest("No settings provided");
            try
            {
                var updated = await _settingsService.UpdateRuntimeSettingsAsync(dto);
                _logger.LogInformation("Runtime settings updated via API");
                return Ok(updated);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to update runtime settings");
                return StatusCode(500, "Failed to update runtime settings");
            }
        }
    }
}

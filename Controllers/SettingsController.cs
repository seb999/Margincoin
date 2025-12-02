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

        [HttpGet]
        public async Task<ActionResult<TradingConfiguration>> Get()
        {
            try
            {
                var config = await _settingsService.GetAsync();
                return Ok(config);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to load trading settings");
                return StatusCode(500, "Failed to load settings");
            }
        }

        [HttpPut]
        public async Task<ActionResult<TradingConfiguration>> Update([FromBody] TradingSettingsDto dto)
        {
            if (dto == null) return BadRequest("No settings provided");
            try
            {
                var updated = await _settingsService.UpdateAsync(dto);
                _logger.LogInformation("Settings updated via API");
                return Ok(updated);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to update trading settings");
                return StatusCode(500, "Failed to update settings");
            }
        }
    }
}

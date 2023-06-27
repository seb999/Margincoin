using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;


namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GlobalsController : ControllerBase
    {
        public GlobalsController()
        {
        }

        // Select Binance server - Prod or test
        [HttpGet("[action]")]
        public bool GetServer()
        {
            return Global.isProd;
        }

        [HttpGet("[action]/{isProd}")]
        public void SetServer(bool isProd)
        {
            Global.isProd = isProd;
        }

        // When server is production, execute real order or not
        [HttpGet("[action]")]
        public bool GetTradingMode()
        {
            return Global.onAir;
        }

        [HttpGet("[action]/{onAir}")]
        public void SetTradingMode(bool onAir)
        {
            Global.onAir = onAir;
        }

        // START and STOP the trading
        [HttpGet("[action]/{isOpen}")]
        public void SetTradeParameter(bool isOpen)
        {
            Global.isTradingOpen = isOpen;
        }

        [HttpGet("[action]")]
        public string GetInterval()
        {
            return JsonSerializer.Serialize(Global.interval);
        }
    }
}


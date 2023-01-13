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
            return Globals.isProd;
        }

        [HttpGet("[action]/{isProd}")]
        public void SetServer(bool isProd)
        {
            Globals.isProd = isProd;
        }

        // When server is production, execute real order or not
        [HttpGet("[action]")]
        public bool GetTradingMode()
        {
            return Globals.onAir;
        }

        [HttpGet("[action]/{onAir}")]
        public void SetTradingMode(bool onAir)
        {
            Globals.onAir = onAir;
        }

        // START and STOP the trading
        [HttpGet("[action]/{isOpen}")]
        public void SetTradeParameter(bool isOpen)
        {
            Globals.isTradingOpen = isOpen;
        }

    }
}


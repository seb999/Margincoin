using System.Collections.Generic;
using System.Text.Json;
using MarginCoin.Model;
using Microsoft.AspNetCore.Mvc;


namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GlobalsController : ControllerBase
    {
        private readonly ApplicationDbContext _appDbContext;

        public GlobalsController([FromServices] ApplicationDbContext appDbContext)
        {
             _appDbContext = appDbContext;
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
            ActionController actionController = new ActionController(_appDbContext);
            Global.SymbolWeTrade = actionController.GetSymbolList();
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


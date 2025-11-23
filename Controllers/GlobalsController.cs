using System.Text.Json;
using MarginCoin.Configuration;
using MarginCoin.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GlobalsController : ControllerBase
    {
        private readonly ITradingState _tradingState;
        private readonly ISymbolService _symbolService;
        private readonly TradingConfiguration _tradingConfig;

        public GlobalsController(
            ITradingState tradingState,
            ISymbolService symbolService,
            IOptions<TradingConfiguration> tradingConfig)
        {
            _tradingState = tradingState;
            _symbolService = symbolService;
            _tradingConfig = tradingConfig.Value;
        }

        [HttpGet("[action]")]
        public bool GetServer() => _tradingState.IsProd;

        [HttpGet("[action]")]
        public bool GetOrderType() => _tradingState.IsMarketOrder;

        [HttpGet("[action]/{isMarketOrder}")]
        public void SetOrderType(bool isMarketOrder)
        {
            _tradingState.IsMarketOrder = isMarketOrder;
        }

        [HttpGet("[action]/{isProd}")]
        public void SetServer(bool isProd)
        {
            _tradingState.IsProd = isProd;
            _tradingState.SymbolWeTrade = _symbolService.GetTradingSymbols();
        }

        [HttpGet("[action]/{isOpen}")]
        public void SetTradeParameter(bool isOpen)
        {
            _tradingState.IsTradingOpen = isOpen;
        }

        [HttpGet("[action]")]
        public string GetInterval() => JsonSerializer.Serialize(_tradingConfig.Interval);
    }
}

using MarginCoin.Class;
using MarginCoin.Misc;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MarginCoin.Service;
using System.Linq;
using MarginCoin.Configuration;
using Microsoft.Extensions.Options;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BinanceController : ControllerBase
    {
        private readonly IHubContext<SignalRHub> _hub;
        private readonly IBinanceService _binanceService;
        private readonly ILogger _logger;
        private readonly ITradingState _tradingState;
        private readonly ISymbolService _symbolService;
        private readonly TradingConfiguration _tradingConfig;

        public BinanceController(IHubContext<SignalRHub> hub,
            ILogger<BinanceController> logger,
            IBinanceService binanceService,
            ITradingState tradingState,
            ISymbolService symbolService,
            IOptions<TradingConfiguration> tradingConfig)
        {
            _hub = hub;
            _binanceService = binanceService;
            _tradingConfig = tradingConfig.Value;
            _logger = logger;
            _tradingState = tradingState;
            _symbolService = symbolService;
        }

        [HttpGet("[action]")]
        public async Task<BinanceAccount> BinanceAccount()
        {
            BinanceAccount myAccount = _binanceService.Account();

            if (myAccount == null)
            {
                await _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.AccessFaulty.ToString());
                return null;
            }

            // Ensure SymbolWeTrade is populated
            if (_tradingState.SymbolWeTrade.Count == 0)
            {
                _tradingState.SymbolWeTrade = _symbolService.GetTopSymbols(_tradingConfig.NumberOfSymbols);
            }

            var symbolWeTrade = _tradingState.SymbolWeTrade;

            // Keep all balances; rank known traded symbols first, then by asset name
            myAccount.balances = myAccount.balances
                .OrderByDescending(b => b.asset == "USDC")
                .ThenBy(b => symbolWeTrade.FirstOrDefault(s => s.SymbolName.Replace("USDC", "").Replace("USDT", "") == b.asset)?.Rank ?? int.MaxValue)
                .ThenBy(b => b.asset)
                .ToList();

            return myAccount;
        }

        [HttpGet("[action]/{symbol}/{qty}")]
        public async Task Sell(string symbol, double qty)
        {
            BinanceOrder myBinanceOrder = _binanceService.SellMarket(symbol, qty);
            if (myBinanceOrder == null) return;

            if (myBinanceOrder.status == "EXPIRED")
            {
                _logger.LogWarning($"Call {MyEnum.BinanceApiCall.SellMarket} {symbol} Expired");

            }

            if (myBinanceOrder.status == "FILLED")
            {
                myBinanceOrder.price = TradeHelper.CalculateAvragePrice(myBinanceOrder).ToString();
                await _hub.Clients.All.SendAsync("sellOrderFilled", JsonSerializer.Serialize(myBinanceOrder));
                return;
            }
            else
            {
                await Task.Delay(300);
                if (_binanceService.OrderStatus(myBinanceOrder.symbol, myBinanceOrder.orderId).status == "FILLED")
                {
                    myBinanceOrder.price = TradeHelper.CalculateAvragePrice(myBinanceOrder).ToString();
                    await _hub.Clients.All.SendAsync("sellOrderFilled", JsonSerializer.Serialize(myBinanceOrder.ToString()));
                }
            }
        }

        [HttpGet("[action]/{symbol}/{quoteQty}")]
        public async Task Buy(string symbol, double quoteQty)
        {
            System.Net.HttpStatusCode httpStatusCode = System.Net.HttpStatusCode.NoContent;
            BinanceOrder myBinanceOrder = _binanceService.BuyMarket(symbol, quoteQty);
            if (myBinanceOrder == null) return;

            if (httpStatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                await _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.BadRequest.ToString());
            }

            if (myBinanceOrder.status == "EXPIRED")
            {
                _logger.LogWarning($"Call {MyEnum.BinanceApiCall.BuyMarket} {symbol} Expired");
            }

            if (myBinanceOrder.status == "FILLED")
            {
                await _hub.Clients.All.SendAsync("buyOrderFilled", JsonSerializer.Serialize(myBinanceOrder));
                return;
            }
            else
            {
                await Task.Delay(300);
                if (_binanceService.OrderStatus(myBinanceOrder.symbol, myBinanceOrder.orderId).status == "FILLED")
                {
                    await _hub.Clients.All.SendAsync("buyOrderFilled", JsonSerializer.Serialize(myBinanceOrder));
                }
            }
        }

        [HttpGet("[action]/{symbol}")]
        public async Task CancelSymbolOrder(string symbol)
        {
            System.Net.HttpStatusCode httpStatusCode = System.Net.HttpStatusCode.NoContent;
            BinanceOrder myBinanceOrder = _binanceService.CancelSymbolOrder(symbol + "USDC");
            if (myBinanceOrder == null) return;

            if (httpStatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                await _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.BadRequest.ToString());
            }

            if (myBinanceOrder.status == "EXPIRED")
            {
                _logger.LogWarning($"Call {MyEnum.BinanceApiCall.CancelOrder} {symbol} Expired");
            }

            if (myBinanceOrder.status == "FILLED")
            {
                await _hub.Clients.All.SendAsync("refreshUI");
                return;
            }
            else
            {
                await Task.Delay(500);
                if (_binanceService.OrderStatus(myBinanceOrder.symbol, myBinanceOrder.orderId).status == "FILLED")
                {
                     await _hub.Clients.All.SendAsync("refreshUI");
                }
            }
        }

        [HttpGet("[action]/{symbol}/{orderId}")]
        public BinanceOrder GetOrderStatus(string symbol, double orderId)
        {
            if (orderId == 0) return null;

            BinanceOrder myBinanceOrder = _binanceService.OrderStatus(symbol, orderId);

            if (myBinanceOrder != null && myBinanceOrder.status == "FILLED")
            {
                myBinanceOrder.price = TradeHelper.CalculateAvragePrice(myBinanceOrder).ToString();
            }

            return myBinanceOrder;
        }
    }
}

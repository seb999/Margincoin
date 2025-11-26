using MarginCoin.Class;
using MarginCoin.Misc;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MarginCoin.Service;
using System.Linq;

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

        public BinanceController(IHubContext<SignalRHub> hub,
            ILogger<BinanceController> logger,
            IBinanceService binanceService,
            ITradingState tradingState,
            ISymbolService symbolService)
        {
            _hub = hub;
            _binanceService = binanceService;
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
                _tradingState.SymbolWeTrade = _symbolService.GetTradingSymbols();
            }

            var symbolWeTrade = _tradingState.SymbolWeTrade;

            // Keep only balances for symbols we trade (+ USDT)
            myAccount.balances = myAccount.balances
                .Where(b => b.asset == "USDT" || symbolWeTrade.Any(s => s.SymbolName.Replace("USDT", "") == b.asset))
                .ToList();

            // Add missing symbols with zero balance
            foreach (var symbol in symbolWeTrade)
            {
                var asset = symbol.SymbolName.Replace("USDT", "");
                if (!myAccount.balances.Any(b => b.asset == asset))
                {
                    myAccount.balances.Add(new balances { asset = asset, free = "0", locked = "0" });
                }
            }

            // Order: USDT first, then by rank
            myAccount.balances = myAccount.balances
                .OrderByDescending(b => b.asset == "USDT")
                .ThenBy(b => symbolWeTrade.FirstOrDefault(s => s.SymbolName.Replace("USDT", "") == b.asset)?.Rank ?? int.MaxValue)
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
            BinanceOrder myBinanceOrder = _binanceService.CancelSymbolOrder(symbol + "USDT");
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
    }
}
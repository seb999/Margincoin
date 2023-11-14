using MarginCoin.Class;
using MarginCoin.Misc;
using MarginCoin.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using Binance.Spot;
using System.Net.WebSockets;
using System.Threading;
using static MarginCoin.Class.Prediction;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MarginCoin.Service;
using System.Linq;
using System.Collections.Generic;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BinanceController : ControllerBase
    {
        private IHubContext<SignalRHub> _hub;
        private IBinanceService _binanceService;
        private readonly ApplicationDbContext _appDbContext;
        private ILogger _logger;

        public BinanceController(IHubContext<SignalRHub> hub,
            [FromServices] ApplicationDbContext appDbContext,
            ILogger<AlgoTradeController> logger,
            IBinanceService binanceService)
        {
            _hub = hub;
            _appDbContext = appDbContext;
            _binanceService = binanceService;
            _logger = logger;
        }

        [HttpGet("[action]")]
        public BinanceAccount BinanceAccount()
        {
            System.Net.HttpStatusCode httpStatusCode = System.Net.HttpStatusCode.NoContent;

            //My asset and quantity available from Binance wallet
            BinanceAccount myAccount = _binanceService.Account(ref httpStatusCode);

            if (httpStatusCode == System.Net.HttpStatusCode.NotFound) _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.BinanceAccessFaulty.ToString());
            if (httpStatusCode == System.Net.HttpStatusCode.NoContent) _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.BinanceAccessFaulty.ToString());
            if (httpStatusCode == System.Net.HttpStatusCode.TooManyRequests) _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.BinanceTooManyRequest.ToString());
            if (httpStatusCode == System.Net.HttpStatusCode.BadRequest) _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.BinanceCheckAllowedIP.ToString());
            if (httpStatusCode == System.Net.HttpStatusCode.Unauthorized) _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.BinanceCheckAllowedIP.ToString());
            if (myAccount == null) return null;

            //Get list of symbol to monitor from DB
            List<Symbol> dbSymbolList = Global.fullSymbolList ? _appDbContext.Symbol.Where(p => p.IsOnProd != 0).ToList()
                                                                : _appDbContext.Symbol.Where(p => p.IsOnTest != 0).ToList();

            //Remove what is not in the db list
            myAccount.balances = myAccount.balances.Where(p => dbSymbolList.Any(p2 => p2.SymbolName.Replace("USDT", "") == p.asset || p.asset == "USDT")).ToList();

            //Add what is not from the db list
            foreach (var item in dbSymbolList)
            {
                if (myAccount.balances.Where(p => p.asset == item.SymbolName.Replace("USDT", "")).FirstOrDefault() == null)
                {
                    myAccount.balances.Add(new balances() { asset = item.SymbolName.Replace("USDT", ""), free = "0", locked = "0" });
                }
            }

            //Filter and keep only the pair that we trade
            myAccount.balances = myAccount.balances.Where(p => Global.SymbolWeTrade.Any(p2 => p2.SymbolName.Replace("USDT", "") == p.asset || p.asset == "USDT")).ToList();

             //Order based on coinmarket rank

             myAccount.balances  = myAccount.balances
            .OrderBy(p1 => dbSymbolList.FirstOrDefault(p2 => p2.SymbolName.Replace("USDT", "") == p1.asset)?.Rank ?? int.MaxValue)
            .ToList();
            
            return myAccount;
        }

        [HttpGet("[action]/{symbol}/{qty}")]
        public async void Sell(string symbol, double qty)
        {
            System.Net.HttpStatusCode httpStatusCode = System.Net.HttpStatusCode.NoContent;
            BinanceOrder myBinanceOrder = _binanceService.SellMarket(symbol, qty, ref httpStatusCode);
            if (myBinanceOrder == null) return;
            if (myBinanceOrder.status == "FILLED")
            {
                await _hub.Clients.All.SendAsync("sellOrderFilled", JsonSerializer.Serialize(myBinanceOrder));
                return;
            }

            if (myBinanceOrder.status == "EXPIRED")
            {
                await _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.BinanceSellOrderExpired.ToString());
                _logger.LogWarning($"Call {MyEnum.BinanceApiCall.SellMarket} {symbol} Expired");

            }

            await Task.Delay(500);
            if (_binanceService.OrderStatus(myBinanceOrder.symbol, myBinanceOrder.orderId).status == "FILLED")
            {
                await _hub.Clients.All.SendAsync("sellOrderFilled", JsonSerializer.Serialize(myBinanceOrder));
            }
        }

        [HttpGet("[action]/{symbol}/{quoteQty}")]
        public async void Buy(string symbol, double quoteQty)
        {
            System.Net.HttpStatusCode httpStatusCode = System.Net.HttpStatusCode.NoContent;
            BinanceOrder myBinanceOrder = _binanceService.BuyMarket(symbol, quoteQty, ref httpStatusCode);
            if (myBinanceOrder == null) return;
            if (myBinanceOrder.status == "FILLED")
            {
                await _hub.Clients.All.SendAsync("buyOrderFilled", JsonSerializer.Serialize(myBinanceOrder));
                return;
            }

            if (myBinanceOrder.status == "EXPIRED")
            {
                await _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.BinanceSellOrderExpired.ToString());
                _logger.LogWarning($"Call {MyEnum.BinanceApiCall.BuyMarket} {symbol} Expired");
            }

            await Task.Delay(500);
            if (_binanceService.OrderStatus(myBinanceOrder.symbol, myBinanceOrder.orderId).status == "FILLED")
            {
                await _hub.Clients.All.SendAsync("buyOrderFilled", JsonSerializer.Serialize(myBinanceOrder));
            }
        }
    }
}
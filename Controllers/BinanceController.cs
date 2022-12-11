using MarginCoin.Class;
using MarginCoin.Misc;
using MarginCoin.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Binance.Spot;
using System.Net.WebSockets;
using System.Threading;
using static MarginCoin.Class.Prediction;
using System.Text.Json;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BinanceController : ControllerBase
    {
        private IHubContext<SignalRHub> _hub;
        private IBinanceService _binanceService;
        private readonly ApplicationDbContext _appDbContext;

        public BinanceController(IHubContext<SignalRHub> hub, 
            [FromServices] ApplicationDbContext appDbContext,
            IBinanceService binanceService)
        {
            _hub = hub;
            _appDbContext = appDbContext;
            _binanceService = binanceService;
        }

        [HttpGet("[action]")]
        public BinanceAccount BinanceAccount()
        {
            System.Net.HttpStatusCode httpStatusCode = System.Net.HttpStatusCode.NoContent;

            //My asset and quantity available from Binance wallet
            //BinanceAccount myAccount = BinanceHelper.Account(ref httpStatusCode);
             BinanceAccount myAccount =_binanceService.Account(ref httpStatusCode);

            if (httpStatusCode == System.Net.HttpStatusCode.NotFound) _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.ApiAccessFaulty.ToString());
            if (httpStatusCode == System.Net.HttpStatusCode.NoContent) _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.ApiAccessFaulty.ToString());
            if (httpStatusCode == System.Net.HttpStatusCode.TooManyRequests) _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.ApiTooManyRequest.ToString());
            if (httpStatusCode == System.Net.HttpStatusCode.BadRequest) _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.ApiCheckAllowedIP.ToString());
            if (myAccount == null) return null;

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
            await Task.Delay(500);
            if (_binanceService.OrderStatus(myBinanceOrder.symbol, myBinanceOrder.orderId).status == "FILLED")
            {
                await _hub.Clients.All.SendAsync("sellOrderFilled", JsonSerializer.Serialize(myBinanceOrder));
            }
        }
    }
}
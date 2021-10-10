using MarginCoin.Misc;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System;


namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AutoTradeController : ControllerBase
    {
        private IHubContext<SignalRHub> _hub;

        public AutoTradeController(IHubContext<SignalRHub> hub)
        {
            _hub = hub;
        }

        [HttpGet("[action]/{symbol}")]
        public void Activate(bool isActivated)
        {
               _hub.Clients.All.SendAsync("newOrder");
            //change setting in db
            if (isActivated) AutoTrading();

        }

        private void AutoTrading()
        {
            //parameters needed from UI
            //Lower / higher / Synbol / quantity
            //do the big brother stuff here:
            //-subscribe to Binance API
            //-on sell or buy oder call the bellow line
             _hub.Clients.All.SendAsync("newOrder");
        }
    }
}
using System.Linq;
using System.Timers;
using MarginCoin.Misc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MarginCoin.Model;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System;
using Tensorflow;

namespace MarginCoin.Service
{
    public class GarbageService
    {
        private ILogger _logger;
        private IHubContext<SignalRHub> _hub;
        private IBinanceService _binanceService;
        private readonly ApplicationDbContext _appDbContext;
        private Timer GarbageTimer = new Timer();

        public GarbageService(ILogger<MLService> logger,
            IHubContext<SignalRHub> hub,
            IBinanceService binanceService,
            [FromServices] ApplicationDbContext appDbContext)
        {
            _logger = logger;
            _hub = hub;
            _appDbContext = appDbContext;
            _binanceService = binanceService;

            GarbageTimer.Interval = 30000;
            GarbageTimer.Elapsed += new ElapsedEventHandler(GarbageTimer_Elapsed);
            GarbageTimer.Start();
        }

        public void StopML()
        {
            GarbageTimer.Stop();
        }

        private void GarbageTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            CheckOrderPool();
        }

        public void ForceGarbageOrder()
        {
            CheckOrderPool();
        }

        private void CheckOrderPool()
        {
            //todo 
            //YOU HAVE TO MOVE HERE methods to SaveOrderDb, updateOrderDb, CloseOrderDB
            //Then what ever type of order you send to market, you check the status here and call the 
            //previous method, and you display popup order in UI and you refrersh UI
            //1 read on local db pending order buy or sell
            var pendingOrderList = _appDbContext.Order.Where(p => p.IsClosed == 0).ToList();

            if (pendingOrderList == null)
            {
                return;
            }
            else
            {
                //For each pending order in local db
                foreach (var pendingOrder in pendingOrderList)
                {
                    //we get the order from binance pool
                    var myBinanceOrder = _binanceService.OrderStatus(pendingOrder.Symbol, pendingOrder.OrderId);

                    //We update the status in local db with binanceOrder status
                    pendingOrder.Status = myBinanceOrder.status;

                    //If the binanceOrder is not filled we kill it if too old or we just save the status in local db
                    if (myBinanceOrder.status != MyEnum.OrderStatus.FILLED.ToString())
                    {
                        DateTime storedDate = DateTime.ParseExact(pendingOrder.OpenDate, "dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                        DateTime currentDateMinusOffset = DateTime.Now.AddSeconds(-50);

                        if (storedDate.CompareTo(currentDateMinusOffset) <= 0)
                        {
                            // we cancel the order
                            pendingOrder.Status = MyEnum.OrderStatus.CANCELED.ToString();

                            _binanceService.CancelOrder(myBinanceOrder.symbol, myBinanceOrder.orderId);
                        }
                    }
                    else
                    {
                        //We have to close the order on db with price / type of close etc....
                    }
                     _appDbContext.Order.Update(pendingOrder);
                    _appDbContext.SaveChanges();

                    _hub.Clients.All.SendAsync("refreshUI");
                }
            }
        }
    }
}
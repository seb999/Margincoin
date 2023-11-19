using System.Linq;
using System.Timers;
using MarginCoin.Misc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MarginCoin.Model;
using Microsoft.AspNetCore.Mvc;
using System;

namespace MarginCoin.Service
{
    public class GarbagePoolService
    {
        private ILogger _logger;
        private IHubContext<SignalRHub> _hub;
        private IBinanceService _binanceService;
        private IOrderService _orderService;
        private readonly ApplicationDbContext _appDbContext;
        private Timer GarbageTimer = new Timer();

        public GarbagePoolService(ILogger<GarbagePoolService> logger,
            IHubContext<SignalRHub> hub,
            IBinanceService binanceService,
            IOrderService orderService,
            [FromServices] ApplicationDbContext appDbContext)
        {
            _logger = logger;
            _hub = hub;
            _appDbContext = appDbContext;
            _binanceService = binanceService;
            _orderService = orderService;

            GarbageTimer.Interval = 30000;
            GarbageTimer.Elapsed += new ElapsedEventHandler(GarbageTimer_Elapsed);
            GarbageTimer.Start();
        }

        public void StopML()
        {
            GarbageTimer.Stop();
        }

        void GarbageTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            CheckPool();
        }

        public void ForceGarbagePool()
        {
            CheckPool();
        }

        private void CheckPool()
        {
            //1 read on local db pending order buy or sell
            var poolOrder = _appDbContext.Order.Where(p => p.Status != MyEnum.OrderStatus.FILLED.ToString()).ToList();

            //No order in the pool
            if (!poolOrder.Any())
                return;

            //For each pending order in local db
            foreach (var order in poolOrder)
            {
                //we get the order status from binance pool
                var myBinanceOrder = _binanceService.OrderStatus(order.Symbol, order.OrderId);
                var storedDate = DateTime.ParseExact(order.OpenDate, "dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                var currentDateMinusOffset = DateTime.Now.AddSeconds(-50);
                var orderStatus = myBinanceOrder.status;
                var orderSide = myBinanceOrder.side;

                if ((orderStatus == "NEW" || orderStatus == "PARTIALLY_FILLED") && storedDate.CompareTo(currentDateMinusOffset) <= 0)
                {
                    _binanceService.CancelOrder(myBinanceOrder.symbol, myBinanceOrder.orderId);
                }

                if (orderStatus == "FILLED")
                {
                    if (orderSide == "BUY")
                    {
                        _orderService.UpdateBuyOrderDb(order.Id, myBinanceOrder);
                    }
                    if (orderSide == "SELL")
                    {
                        _orderService.UpdateSellOrderDb(order.Id, myBinanceOrder);
                        _orderService.CloseOrderDb(order.Id, myBinanceOrder);
                    }
                }

                if (orderStatus == "PARTIALLY_FILLED")
                {
                    if (orderSide == "BUY")
                    {
                        _orderService.UpdateBuyOrderDb(order.Id, myBinanceOrder);
                    }
                    if (orderSide == "SELL")
                        _orderService.UpdateSellOrderDb(order.Id, myBinanceOrder);
                }

                if (orderStatus == "CANCELED" || orderStatus == "REJECTED" || orderStatus == "EXPIRED")
                {
                    if (orderSide == "BUY")
                    {
                        if (myBinanceOrder.executedQty == "")
                        {
                            Global.onHold.Remove(order.Symbol);
                            _orderService.DeleteOrder(order.Id);
                        }
                        else
                        {
                            _orderService.UpdateBuyOrderDb(order.Id, myBinanceOrder);
                             _orderService.RecycleOrderDb(order.Id);
                        }
                    }
                    if (orderSide == "SELL")
                    {
                        if (myBinanceOrder.executedQty == "")
                        {
                            _orderService.UpdateTypeDb(order.Id, "");
                            _orderService.RecycleOrderDb(order.Id); 
                            Global.onHold.Remove(order.Symbol);
                        }
                        else
                        {
                            _orderService.UpdateSellOrderDb(order.Id, myBinanceOrder);
                            Global.onHold.Remove(order.Symbol);
                        }
                    }
                }
            }

            _hub.Clients.All.SendAsync("refreshUI");
        }
    }
}
using MarginCoin.Class;
using MarginCoin.Misc;
using MarginCoin.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WebSocketSharp;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AutoTradeController : ControllerBase
    {
        private IHubContext<SignalRHub> _hub;
        private readonly ApplicationDbContext _appDbContext;
        private static Boolean isAutoTradeActivated = false;
        private List<Quotation> quotationList = new List<Quotation>();
        private string streamInterval;
        private double takeProfitDynamic;
        private bool secureProfit;
        private bool buyUnlock1;

        public AutoTradeController(IHubContext<SignalRHub> hub, [FromServices] ApplicationDbContext appDbContext)
        {
            _hub = hub;
            _appDbContext = appDbContext;
            streamInterval = "3m";
            takeProfitDynamic = 0;
        }

        [HttpGet("[action]")]
        public void StopTrade()
        {
            isAutoTradeActivated = false;
        }

        [HttpGet("[action]/{Symbol}")]
        public async Task<string> StartTrade(string symbol)
        {
            //2-get historic data
            GetCandleData(symbol);

            //3-Start streaming and attach last data to list, calculate indicator and pass orders
            isAutoTradeActivated = true;
            var ws = new WebSocket("wss://stream.binance.com:9443/ws/" + symbol.ToLower() + "@kline_" + streamInterval);
            ws.OnMessage += ws_OnMessage;
            ws.Connect();
            while (isAutoTradeActivated && !HttpContext.RequestAborted.IsCancellationRequested)
            {
                await Task.Delay(2000);
            }
            ws.Close();
            await _hub.Clients.All.SendAsync("tradingStopped");
            return "";
        }

        private void ws_OnMessage(Object sender, MessageEventArgs e)
        {
            UpdateCandleData(Helper.deserializeHelper<StreamData>(e.Data));

            AlgoTrading();
        }

        private void AlgoTrading()
        {
            int i = 0;
            //Update Client
            _hub.Clients.All.SendAsync("trading", quotationList.Last().Rsi, quotationList.Last().PivotPoint.R1, quotationList.Last().PivotPoint.S1);

            //Read active order
            Order activeOrder = GetActiveOrder();

            //Read last quotation
            Quotation last = quotationList.Last();
        
            //Open new order if there is no
            if (activeOrder == null)
            {
                //if (last.x && last.c > last.PivotPoint.R1)
                if (last.c > last.PivotPoint.R1)
                {
                    buyUnlock1 = true;
                }
                if (buyUnlock1)
                {
                    if (last.MacdHist > 0)
                    {
                        i++;
                    }
                    if (last.Rsi > 50)
                    {
                        i++;
                    }
                    if (last.c > last.Ema)
                    {
                        i++;
                    }
                    if (i == 3)
                    {
                        OpenOrder();
                    }
                }
            }
            if (activeOrder != null)
            {
                //Stop lose
                if (last.c <= (activeOrder.OpenPrice * (1 - (activeOrder.StopLose / 100))))
                {
                    CloseOrder(activeOrder.Id);
                }

                //Take profit : no lose mode
                if (secureProfit)
                {
                    if (last.c < takeProfitDynamic)
                    {
                        CloseOrder(activeOrder.Id);
                    }

                    double pendingProfit = (last.c - activeOrder.OpenPrice) * activeOrder.Quantity;

                    if (pendingProfit <= activeOrder.Fee * 2.5)
                    {
                        CloseOrder(activeOrder.Id);
                    }

                    takeProfitDynamic = last.c - (last.c * 0.0015);
                    // if (last.c >= last.PivotPoint.R1)
                    // {
                    //     takeProfitDynamic = last.PivotPoint.R1;
                    // }
                    // else
                    // {
                    //      takeProfitDynamic = last.c - (last.c * 0.0015);
                    //     // if (pendingProfit <= 30)
                    //     //     takeProfitDynamic = last.c - (last.c * 0.003);
                    //     // if (pendingProfit > 30 && pendingProfit <= 60)
                    //     //     takeProfitDynamic = last.c - (last.c * 0.003);
                    //     // if (pendingProfit > 60 && pendingProfit <= 100)
                    //     //     takeProfitDynamic = last.c - (last.c * 0.003);
                    //     // if (pendingProfit > 100 && pendingProfit <= 200)
                    //     //     takeProfitDynamic = last.c - (last.c * 0.002);
                    //     // if (pendingProfit > 200)
                    //     //     takeProfitDynamic = last.c - (last.c * 0.0015);
                    // }



                }
                //Unlock take profit
                if (last.c >= activeOrder.TakeProfit && !secureProfit)
                {
                    secureProfit = true;
                }
            }
        }

        private void OpenOrder()
        {
            //0-block double open order if saving to db takes too much time
            Order activeOrder = GetActiveOrder();
            if(activeOrder!=null) return;

            //1-read from db template
            OrderTemplate orderTemplate = GetOrderTemplate();

            double quantity = orderTemplate.Amount / quotationList.Last().c;

            Order myOrder = new Order();
            myOrder.OpenDate = DateTime.Now.ToString();
            myOrder.OpenPrice = quotationList.Last().c;
            myOrder.TakeProfit = quotationList.Last().c * 1.008;
            myOrder.Quantity = quantity;
            myOrder.IsClosed = 0;
            myOrder.Margin = 1;
            myOrder.StopLose = orderTemplate.StopLose;
            myOrder.Fee = Math.Round((quotationList.Last().c * quantity) / 100) * 0.1;
            myOrder.Symbol = quotationList.Last().s;
            myOrder.RSIIn = quotationList.Last().Rsi;

            _appDbContext.Order.Add(myOrder);
            _appDbContext.SaveChanges();
            _hub.Clients.All.SendAsync("refreshUI", 0, 0, 0);

            secureProfit = false;
            buyUnlock1 = false;
        }

        public void CloseOrder(double orderId)
        {
            Order myOrder = _appDbContext.Order.Where(p => p.Id == orderId).Select(p => p).FirstOrDefault();
            myOrder.ClosePrice = quotationList.Last().c;
            myOrder.Fee = myOrder.Fee + Math.Round((quotationList.Last().c * myOrder.Quantity) / 100 * 0.1);
            myOrder.IsClosed = 1;
            myOrder.Profit = Math.Round((quotationList.Last().c - myOrder.OpenPrice) * myOrder.Quantity * myOrder.Margin);
            myOrder.CloseDate = DateTime.Now.ToString();
            myOrder.RSIOut = quotationList.Last().Rsi;

            _appDbContext.SaveChanges();
            _hub.Clients.All.SendAsync("refreshUI", 0, 0, 0);

            secureProfit = false;
            buyUnlock1 = false;
        }

        [HttpGet("[action]")]
        public OrderTemplate GetOrderTemplate()
        {
            return _appDbContext.OrderTemplate.Select(p => p).FirstOrDefault();
        }

        private Order GetActiveOrder()
        {
            return _appDbContext.Order.Where(p => p.IsClosed == 0).FirstOrDefault();
            //return _appDbContext.Order.Where(p => p.Symbol == quotationList.Last().s && p.IsClosed == 0).FirstOrDefault();
        }

        private void GetCandleData(string symbol)
        {
            string apiUrl = string.Format("https://api3.binance.com/api/v3/klines?symbol={0}&interval=" + streamInterval + "&limit=500", symbol);

            //Get data from Binance API
            List<List<double>> coinQuotation = HttpHelper.GetApiData<List<List<double>>>(new Uri(apiUrl));

            foreach (var item in coinQuotation)
            {
                Quotation newQuotation = new Quotation()
                {
                    T = item[0],
                    o = item[1],
                    h = item[2],
                    l = item[3],
                    c = item[4],
                    v = item[5],
                    t = item[6],
                };
                quotationList.Add(newQuotation);
            }

            //Add Indicators to the list
            TradeIndicator.CalculateIndicator(ref quotationList);
        }

        private void UpdateCandleData(StreamData lastQuote)
        {
            //We add the last received candle
            quotationList.Add(lastQuote.k);
            quotationList.RemoveAt(0);

            //Add Indicators to the list
            TradeIndicator.CalculateIndicator(ref quotationList);
        }

        [HttpPost("[action]")]
        public bool SaveOrderTemplate([FromBody] OrderTemplate orderTemplate)
        {

            try
            {
                OrderTemplate myOrderTemplate = _appDbContext.OrderTemplate.Select(p => p).FirstOrDefault();
                if (myOrderTemplate != null)
                {
                    myOrderTemplate.Amount = orderTemplate.Amount;
                    myOrderTemplate.Margin = orderTemplate.Margin;
                    myOrderTemplate.Quantity = orderTemplate.Quantity;
                    myOrderTemplate.StopLose = orderTemplate.StopLose;
                    orderTemplate.IsInactive = 0;
                    myOrderTemplate.DateMod = DateTime.Now.ToString();
                }
                else
                {
                    orderTemplate.DateAdded = DateTime.Now.ToString();
                    orderTemplate.IsInactive = 0;
                    _appDbContext.OrderTemplate.Add(orderTemplate);
                }
                _appDbContext.SaveChanges();
            }
            catch (System.Exception ex)
            {
                return false;
            }

            return true;
        }



    }
}
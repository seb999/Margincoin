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
        private static Boolean isActiveOrder = false;
        private List<Quotation> quotationList = new List<Quotation>();
        private string streamInterval;

        public AutoTradeController(IHubContext<SignalRHub> hub, [FromServices] ApplicationDbContext appDbContext)
        {
            _hub = hub;
            _appDbContext = appDbContext;
            streamInterval = "15m";
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
            _hub.Clients.All.SendAsync("trading");

            //2-Check active order
            Order activeOrder = GetActiveOrder();

            //Do some calculation
            Quotation last = quotationList.Last();

            if (activeOrder == null)
            {
                if (last.MacdHist > 0)
                {
                    i++;
                }
                if (last.Rsi > 40)
                {
                    i++;
                }
                if (last.c > last.Ema)
                {
                    i++;
                }
                // if(last.x == true)
                // {
                //     i++;
                // }
                if (last.c > last.PivotPoint.R1 || last.c > last.PivotPoint.R2 || last.c > last.PivotPoint.R3)
                {
                    i++;
                }
                if (i == 4)
                {
                    OpenOrder();
                }
                else
                {
                    i = 0;
                }
            }
            else
            {
                if (last.c >= activeOrder.TakeProfit)
                {
                    CloseOrder(activeOrder.Id);
                }
            }
        }

        private void OpenOrder()
        {
            //1-read from db template
            OrderTemplate orderTemplate = GetOrderTemplate();

            Order newBuyOrder = new Order();
            newBuyOrder.OpenDate = DateTime.Now.ToString();
            newBuyOrder.OpenPrice = quotationList.Last().c;
            newBuyOrder.TakeProfit = quotationList.Last().c*1.004;
            newBuyOrder.Quantity = orderTemplate.Quantity;
            newBuyOrder.IsClosed = 0;
            newBuyOrder.Margin = 1;
            newBuyOrder.StopLose = 0 ; //to be defined
            newBuyOrder.Fee = Math.Round((quotationList.Last().c * orderTemplate.Quantity) /100) * 0.1;
            newBuyOrder.Symbol = quotationList.Last().s;
            _appDbContext.Order.Add(newBuyOrder);
            _appDbContext.SaveChanges();

            _hub.Clients.All.SendAsync("refreshUI");
        }

        public void CloseOrder(double orderId)
        {
            Order myOrder = _appDbContext.Order.Where(p => p.Id == orderId).Select(p => p).FirstOrDefault();
            myOrder.ClosePrice = quotationList.Last().c;
            myOrder.Fee = myOrder.Fee + Math.Round((quotationList.Last().c * myOrder.Quantity) /100) * 0.1;
            myOrder.IsClosed = 1;
            myOrder.Profit = Math.Round((quotationList.Last().c - myOrder.OpenPrice) * myOrder.Quantity * myOrder.Margin);
            myOrder.CloseDate = DateTime.Now.ToString();
            _appDbContext.SaveChanges();

             _hub.Clients.All.SendAsync("refreshUI");
        }

        private OrderTemplate GetOrderTemplate()
        {
            return _appDbContext.OrderTemplate.Where(p => p.Symbol == quotationList.Last().s).FirstOrDefault();
        }

        private Order GetActiveOrder()
        {
            return _appDbContext.Order.Where(p => p.Symbol == quotationList.Last().s && p.IsClosed == 0).FirstOrDefault();
            // if (_appDbContext.Order.Where(p => p.Symbol == quotationList.Last().s && p.IsClosed == 0).ToList().Count > 0)
            // {
            //     isActiveOrder = true;
            // }
            // else
            // {
            //     isActiveOrder = false;
            // }
            // return isActiveOrder;
        }

        private void GetCandleData(string symbol)
        {
            string apiUrl = string.Format("https://api3.binance.com/api/v3/klines?symbol={0}&interval=" + streamInterval + "&limit=200", symbol);

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
                orderTemplate.DateAdded = DateTime.Now.ToString();
                orderTemplate.IsInactive = 0;
                _appDbContext.OrderTemplate.Add(orderTemplate);
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
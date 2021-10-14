using MarginCoin.Misc;
using MarginCoin.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
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

        public AutoTradeController(IHubContext<SignalRHub> hub, [FromServices] ApplicationDbContext appDbContext)
        {
            _hub = hub;
            _appDbContext = appDbContext;
        }

        [HttpGet("[action]")]
        public void StopTrade()
        {
             isAutoTradeActivated = false;
        }

        [HttpGet("[action]")]
        public async Task<string> StartTrade()
        {
            //1-read from db template

            //2-get historic data

            //3-Start streaming and attach last data to list, calculate indicator and BigBrother
            isAutoTradeActivated = true;
            var ws = new WebSocket("wss://stream.binance.com:9443/ws/btcusdt@kline_4h");
            ws.OnMessage += ws_OnMessage;
            ws.Connect();
            while (isAutoTradeActivated && !HttpContext.RequestAborted.IsCancellationRequested)
            {
                await Task.Delay(2000);
            }
            ws.Close();
            return "";
        }

        private void ws_OnMessage(Object sender, MessageEventArgs e)
        {
            //_hub.Clients.All.SendAsync("newOrder");
            GetLastQuote("BTCUSDT");
        }

        private Quotation GetLastQuote(string symbol)
        {
            List<Quotation> quotationList = new List<Quotation>();
            string apiUrl = string.Format("https://api3.binance.com/api/v3/klines?symbol={0}&interval=4h&limit=1000", symbol);
            // https://api3.binance.com/api/v3/klines?symbol=BTCUSDT&interval=4h

            //Get data from Binance API
            List<List<double>> coinQuotation = HttpHelper.GetApiData<List<List<double>>>(new Uri(apiUrl));

            foreach (var item in coinQuotation)
            {
                Quotation newQuotation = new Quotation()
                {
                    E = item[0],
                    o = item[1],
                    h = item[2],
                    l = item[3],
                    c = item[4],
                    v = item[5],
                };
                quotationList.Add(newQuotation);
            }
            //Add Indicators to the list
            TradeIndicator.CalculateIndicator(ref quotationList);

            //Add Default Prediction
            var result = quotationList.Last();

            return result;


            //parameters needed from UI
            //Lower / higher / Synbol / quantity
            //do the big brother stuff here:
            //-subscribe to Binance API
            //-Calculate indicators when you have enought data MACD, RSI, EMA
            //-create the Algo 

            //Example how to send an event to UI
            _hub.Clients.All.SendAsync("newOrder");
        }

        [HttpGet("[action]/{symbol}")]
        public OrderTemplate GetOrderTemplate(string symbo)
        {
            return _appDbContext.OrderTemplate.Where(p => p.IsInactive != 1).Select(p => p).FirstOrDefault();
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

//1-Get candles
//https://api3.binance.com/api/v3/klines?symbol=BTCUSDT&interval=4h

//2-Add last point
// wss://stream.binance.com:9443/ws/btcusdt@kline_4h

//3-remove first point of the list if you are smart


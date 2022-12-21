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
using Microsoft.Extensions.Logging;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AutoTrade3Controller : ControllerBase
    {

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////------------Global varibles----------//////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        private IHubContext<SignalRHub> _hub;
        private IBinanceService _binanceService;
        private readonly ApplicationDbContext _appDbContext;
        private ILogger _logger;
        private List<List<Candle>> candleMatrice = new List<List<Candle>>();
        private List<MarketStream> marketStreamOnSpot = new List<MarketStream>();
        private List<string> mySymbolList = new List<string>();
        int nbrUp = 0;
        int nbrDown = 0;
        private bool exportStarted = false;

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////------------SETTINGS----------/////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        string interval = "1h";   //1h seem to give better result

        int numberPreviousCandle = 3;

        double stopLose = 0.5;

        double takeProfit = 1;

        //max trade that can be open
        int maxOpenTrade = 5;

        int quoteOrderQty = 2000;

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        ///////////////////////////////-----------Constructor----------/////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        public AutoTrade3Controller(
            IHubContext<SignalRHub> hub, 
            [FromServices] ApplicationDbContext appDbContext, 
            ILogger<AutoTrade3Controller> logger,
            IBinanceService binanceService)
        {
            _hub = hub;
            _binanceService = binanceService;
            _appDbContext = appDbContext;
            _logger = logger;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////////-----------ALGORYTME----------/////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////

        [HttpGet("[action]")]
        public async Task<string> MonitorMarket()
        {
             _logger.LogWarning("Start trading market...");

            //Get the list of symbol that we agree to trade from DB
            mySymbolList = GetSymbolList();

            //Get historic candles and open a webSocket for each symbol in my list
            foreach (var symbol in mySymbolList)
            {
                GetCandles(symbol);

                OpenWebSocketOnSymbol(symbol);
            }

            //Open web socket on spot to get 24h change stream
            await OpenWebSocketOnSpot();

            return "";
        }

        [HttpGet("[action]/{orderId}")]
        public string CloseTrade(int orderId)
        {
            Order myOrder = _appDbContext.Order.Where(p => p.Id == orderId).Select(p => p).FirstOrDefault();
            if (myOrder != null)
            {
                Sell(orderId, "by user");
            }

            return "";
        }

        private async void OpenWebSocketOnSymbol(string symbol)
        {
            MarketDataWebSocket ws = new MarketDataWebSocket($"{symbol.ToLower()}@kline_{interval}");
            var onlyOneMessage = new TaskCompletionSource<string>();

            ws.OnMessageReceived(
                (data) =>
            {
                data = data.Remove(data.IndexOf("}}") + 2);
                var stream = Helper.deserializeHelper<StreamData>(data);
                var symbolIndex = 0;

                //get corresponding line in our Matrice
                for (int i = 0; i < candleMatrice.Count; i++)
                {
                    if (candleMatrice[i][0].s == stream.k.s)
                    {
                        symbolIndex = i;
                        break;
                    }
                }

                if (!stream.k.x)
                {
                    if (candleMatrice[symbolIndex].Count > 0) candleMatrice[symbolIndex] = candleMatrice[symbolIndex].SkipLast(1).ToList();
                    ExportChart(false);
                }
                else
                {
                    Console.WriteLine($"New candle save : {stream.k.s}");
                    Globals.onHold.Remove(stream.k.s);
                    ExportChart(true);
                }

                Candle newCandle = new Candle()
                {
                    s = stream.k.s,
                    o = stream.k.o,
                    h = stream.k.h,
                    l = stream.k.l,
                    c = stream.k.c,
                };
                candleMatrice[symbolIndex].Add(newCandle);
                List<Candle> candleListWithIndicators = TradeIndicator.CalculateIndicator(candleMatrice[symbolIndex]);
                candleMatrice[symbolIndex] = candleListWithIndicators;

                return Task.CompletedTask;

            }, CancellationToken.None);

            try
            {
                await ws.ConnectAsync(CancellationToken.None);
                string message = await onlyOneMessage.Task;
                await ws.DisconnectAsync(CancellationToken.None);
            }
            catch
            {
                Console.WriteLine("impossible to open websocket on " + symbol);
            }
            finally
            {
                ws = new MarketDataWebSocket($"{symbol.ToLower()}@kline_{interval}");
            }
        }

        public async Task<string> OpenWebSocketOnSpot()
        {
            MarketDataWebSocket ws1 = new MarketDataWebSocket("!ticker@arr");
            var onlyOneMessage = new TaskCompletionSource<string>();
            string dataResult = "";

            ws1.OnMessageReceived(
                (data) =>
            {
                dataResult += data;
                if (dataResult.Contains("}]"))
                {
                    if (dataResult.Length > (dataResult.IndexOf("]") + 1))
                    {
                        dataResult = dataResult.Remove(dataResult.IndexOf("]") + 1);
                    }

                    List<MarketStream> marketStreamList = Helper.deserializeHelper<List<MarketStream>>(dataResult);
                    marketStreamList = marketStreamList.Where(p => p.s.Contains("USDT") && !p.s.Contains("DOWNUSDT")).Select(p => p).OrderByDescending(p => p.P).ToList();

                    AutotradeHelper.BufferMarketStream(marketStreamList, ref marketStreamOnSpot);

                    nbrUp = marketStreamOnSpot.Where(pred => pred.P >= 0).Count();
                    nbrDown = marketStreamOnSpot.Where(pred => pred.P < 0).Count();

                    ProcessMarketMatrice();

                    dataResult = "";
                }
                return Task.CompletedTask;

            }, CancellationToken.None);

            await ws1.ConnectAsync(CancellationToken.None);
            string message = await onlyOneMessage.Task;
            await ws1.DisconnectAsync(CancellationToken.None);

            return "";
        }

        private void ProcessMarketMatrice()
        {
            try
            {
                //0-Check each open order if closing is needed
                List<Order> activeOrderList = GetActiveOrder();
                if (activeOrderList.Count != 0)
                {
                    activeOrderList.Where(p => CheckTrade(p)).ToList();
                }

                //1- Filter marketStreamOnSpot list to get only the one from mySymbolList
                marketStreamOnSpot = marketStreamOnSpot.Where(p => mySymbolList.Any(p1 => p1 == p.s)).OrderByDescending(p => p.P).ToList();

                //Send last data to frontend
                _hub.Clients.All.SendAsync("trading", JsonSerializer.Serialize(marketStreamOnSpot));

                if (activeOrderList.Count == maxOpenTrade) return;

                //2-check candle (indicators, etc, not on hold ) and invest
                for (int i = 0; i < maxOpenTrade - activeOrderList.Count; i++)
                {
                    var symbol = marketStreamOnSpot[i].s;

                    //if there is already a pending order for this symbol we exit
                    if (activeOrderList.Where(p => p.Symbol == symbol).Select(p => p).ToList().Count != 0)
                    {
                        continue;
                    }

                    List<Candle> symbolCandle = candleMatrice.Where(p => p.First().s == symbol).FirstOrDefault();  //get the line that coorespond to the symbol

                    //if (Globals.swallowOneOrder)
                    if((CheckCandle(numberPreviousCandle, marketStreamOnSpot[i], symbolCandle) && !Globals.onHold.FirstOrDefault(p => p.Key == symbol).Value))
                    {
                        //Globals.swallowOneOrder = false;
                        if (Globals.isTradingOpen)
                        {
                            Console.WriteLine($"Open trade on {symbol}");
                            if (!Globals.onHold.ContainsKey(symbol)) Globals.onHold.Add(symbol, true);  //to avoid multi buy
                            Buy(marketStreamOnSpot[i], symbolCandle);
                        }
                        else
                        {
                            Console.WriteLine($"Trading closed by user");
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, " ProcessMarketMatrice");
                return;
            }
        }

        private bool CheckCandle(int numberCandle, MarketStream symbolSpot, List<Candle> symbolCandle)
        {   //Question : why using the marketFirstCoin parameter as we have the last value in the last candle in the list

            if (symbolCandle.Count < 2) return false;

            if (symbolSpot.s != symbolCandle.Last().s)
            {
                Console.WriteLine("Inconsistancy in candle list");
                return false;
            }

            //0 - Don't trade if only 14% coins are up over the last 24h AND coin is slittly negatif
            //    If the coin is very negatif over last 24h we are in a dive and we want to trade at reversal tendance
            if ((double)nbrUp < 30 && symbolSpot.P > -5)
            {
                return false;
            }

            //1 - Previous candles are green
            for (int i = symbolCandle.Count - numberCandle; i < symbolCandle.Count; i++)
            {
                Console.WriteLine(AutotradeHelper.CandleColor(symbolCandle[i]));
                if ((AutotradeHelper.CandleColor(symbolCandle[i]) == "green") && symbolCandle[i].c > symbolCandle[i - 1].c)
                {
                    continue;
                }
                else
                {
                    return false;
                }
            }

            //2 - The current should be higther than the previous candle + 1/10
            if (symbolSpot.c < (symbolCandle[symbolCandle.Count - 2].c + (symbolCandle[symbolCandle.Count - 2].h - symbolCandle[symbolCandle.Count - 2].c) / 8))
            {
                return false;
            }

            //3 - If MACD 100 don't buy
            if (symbolCandle.Last().Macd == 100)
            {
                return false;
            }

            //4 - RSI should be lower than 72 or RSI lower 80 if we already trade the coin
            //if (symbolCandle.Last().Rsi < 56)
            if (symbolCandle.Last().Rsi < 70)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool CheckTrade(Order activeOrder)
        {
            double lastPrice = 0;
            double highPrice = 0;
            Candle lastCandle = new Candle();

            //iteration the matrice to find the line for the symbol of the active order
            List<Candle> symbolCandle = candleMatrice.Where(p => p.First().s == activeOrder.Symbol).FirstOrDefault();
            int symbolCandleIndex = candleMatrice.IndexOf(symbolCandle); //get the line that coorespond to the symbol]
            lastPrice = symbolCandle.Select(p => p.c).LastOrDefault();
            highPrice = symbolCandle.Select(p => p.h).LastOrDefault();
            lastCandle = symbolCandle.Select(p => p).LastOrDefault();

             SaveHighLow(lastCandle, activeOrder);

            if (lastPrice <= (activeOrder.StopLose))
            {
                Console.WriteLine("Close trade : stop lose ");
                Sell(activeOrder.Id, "stop lose");
            }

            //After +2% we use a take profit
            if (lastPrice > activeOrder.OpenPrice * 1.02)
            {
                if (lastPrice <= (activeOrder.HighPrice * (1 - (activeOrder.TakeProfit / 100))))
                {
                    Console.WriteLine("Close trade : take profit ");
                    Sell(activeOrder.Id, "Take profit");
                }
            }

            UpdateStopLose(lastPrice, activeOrder);

            UpdateTakeProfit(lastPrice, activeOrder);

            return true;
        }

        private void UpdateTakeProfit(double lastPrice, Order activeOrder)
        {
            double pourcent = ((lastPrice - activeOrder.OpenPrice) / activeOrder.OpenPrice) * 100;

            if (pourcent <= 0) return;

            if (2 < pourcent && pourcent <= 3)
            {
                activeOrder.TakeProfit = takeProfit - 0.2;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }

            if (3 < pourcent && pourcent <= 4)
            {
                activeOrder.TakeProfit = takeProfit - 0.3;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }
            if (4 < pourcent && pourcent <= 5)
            {
                activeOrder.TakeProfit = takeProfit - 0.4;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }
            if (5 < pourcent)
            {
                activeOrder.TakeProfit = takeProfit - 0.5;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }
        }

        private void UpdateStopLose(double lastPrice, Order activeOrder)
        {
            if (lastPrice >= activeOrder.OpenPrice * 1.02)
            {
                activeOrder.StopLose = activeOrder.OpenPrice * 1.015;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
                return;
            }

            if (lastPrice >= activeOrder.OpenPrice * 1.015)
            {
                activeOrder.StopLose = activeOrder.OpenPrice * 1.01;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
                return;
            }

            if (lastPrice >= activeOrder.OpenPrice * 1.01)
            {
                activeOrder.StopLose = activeOrder.OpenPrice * 1.005;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
                return;
            }

            if (lastPrice >= activeOrder.OpenPrice * 1.005)
            {
                activeOrder.StopLose = activeOrder.OpenPrice;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
                return;
            }
        }


        #region Buy / Sell

        private async void Buy(MarketStream symbolSpot, List<Candle> symbolCandleList)
        {
            System.Net.HttpStatusCode httpStatusCode = System.Net.HttpStatusCode.NoContent;
            BinanceOrder myBinanceOrder = _binanceService.BuyMarket(symbolSpot.s, quoteOrderQty, ref httpStatusCode);

            if (myBinanceOrder == null) 
            {
                Globals.onHold.Remove(symbolSpot.s);
                return;
            }

            if (httpStatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                _logger.LogWarning($"Call {MyEnum.BinanceApiCall.BuyMarket} {symbolSpot.s} Locked");
                return;
            }

            if(myBinanceOrder.status == "EXPIRED")
            {
                await _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.sellOrderExired.ToString());
                _logger.LogWarning($"Call {MyEnum.BinanceApiCall.BuyMarket} {symbolSpot.s} Expired");
                Globals.onHold.Remove(symbolSpot.s);
                return;
                
            }
            int i = 0;
            while (myBinanceOrder.status != "FILLED")
            {
                if (i == 5) break;
                myBinanceOrder = _binanceService.OrderStatus(myBinanceOrder.symbol, myBinanceOrder.orderId);
                i++;
            }

            if (myBinanceOrder.status == "FILLED")
            {
                //Save here binance order result in db
                SaveTrade(symbolSpot, symbolCandleList, myBinanceOrder);
                Globals.onHold.Remove(symbolSpot.s);
                await _hub.Clients.All.SendAsync("newPendingOrder", JsonSerializer.Serialize(myBinanceOrder));
                await Task.Delay(500);
                await _hub.Clients.All.SendAsync("newOrder");
            }
        }

        private async void Sell(double id, string closeType)
        {
            System.Net.HttpStatusCode httpStatusCode = System.Net.HttpStatusCode.NoContent;

            Order myOrder = _appDbContext.Order.Where(p => p.Id == id).Select(p => p).FirstOrDefault();
            BinanceOrder myBinanceOrder = _binanceService.SellMarket(myOrder.Symbol, myOrder.Quantity, ref httpStatusCode);

            if (myBinanceOrder == null) return;

            int i = 0;
            while (myBinanceOrder.status != "FILLED")
            {
                if (i == 5) break;
                myBinanceOrder = _binanceService.OrderStatus(myBinanceOrder.symbol, myBinanceOrder.orderId);
                i++;
            }

            if (myBinanceOrder.status == "FILLED")
            {
                CloseTrade(id, closeType, myBinanceOrder);
                await Task.Delay(500);
                await _hub.Clients.All.SendAsync("sellOrderFilled", JsonSerializer.Serialize(myBinanceOrder));
            }
        }

        private void SaveTrade(MarketStream symbolSpot, List<Candle> symbolCandle, BinanceOrder binanceOrder)
        {
            Console.WriteLine("Open trade");

            //Debug : something wrong with path of model on mac
            //List<ModelOutput> prediction = AIHelper.GetPrediction(symbolCandle);

            Order myOrder = new Order();
            myOrder.OpenDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            myOrder.OpenPrice = CalculateAvragePrice(binanceOrder);
            myOrder.HighPrice = CalculateAvragePrice(binanceOrder);
            myOrder.LowPrice = CalculateAvragePrice(binanceOrder);
            myOrder.Volume = symbolSpot.v;
            myOrder.TakeProfit = takeProfit;
            myOrder.StopLose = CalculateAvragePrice(binanceOrder) * (1 - (stopLose / 100));
            myOrder.Quantity = double.Parse(binanceOrder.executedQty);
            myOrder.IsClosed = 0;
            myOrder.Fee = Globals.isProd ? binanceOrder.fills.Sum(p => double.Parse(p.commission)) : Math.Round((CalculateAvragePrice(binanceOrder) * double.Parse(binanceOrder.executedQty)) / 100) * 0.1;
            myOrder.Symbol = binanceOrder.symbol;

            myOrder.RSI = symbolCandle.Last().Rsi;
            myOrder.RSI_1 = symbolCandle[symbolCandle.Count - 2].Rsi;
            myOrder.RSI_2 = symbolCandle[symbolCandle.Count - 3].Rsi;
            myOrder.EMA = symbolCandle.Last().Ema;
            myOrder.StochSlowD = symbolCandle.Last().StochSlowD;
            myOrder.StochSlowK = symbolCandle.Last().StochSlowK;
            myOrder.MACD = symbolCandle.Last().Macd;
            myOrder.MACDSign = symbolCandle.Last().MacdSign;
            myOrder.MACDHist = symbolCandle.Last().MacdHist;
            myOrder.MACDHist_1 = symbolCandle[symbolCandle.Count - 2].MacdHist;
            myOrder.MACDHist_2 = symbolCandle[symbolCandle.Count - 3].MacdHist;
            myOrder.MACDHist_3 = symbolCandle[symbolCandle.Count - 4].MacdHist;
            // myOrder.PredictionLBFGS = prediction[0].Prediction == true ? 1 : 0;
            // myOrder.PredictionLDSVM = prediction[1].Prediction == true ? 1 : 0;
            // myOrder.PredictionSDA = prediction[2].Prediction == true ? 1 : 0;

            myOrder.Lock = 0;
            myOrder.MarketTrend = $"{nbrUp}|{nbrDown}";
            myOrder.Status = "FILLED";
            myOrder.OrderId = binanceOrder.orderId;

            _appDbContext.Order.Add(myOrder);
            _appDbContext.SaveChanges();
        }

        private void CloseTrade(double orderId, string closeType, BinanceOrder binanceOrder)
        {
            Order myOrder = _appDbContext.Order.Where(p => p.Id == orderId).Select(p => p).FirstOrDefault();
            if (!Globals.onHold.ContainsKey(myOrder.Symbol)) Globals.onHold.Add(myOrder.Symbol, true);

            myOrder.ClosePrice = CalculateAvragePrice(binanceOrder);
            //myOrder.Fee += binanceOrder.fills.Sum(P => long.Parse(P.commission));
            myOrder.Profit = Math.Round((myOrder.ClosePrice - myOrder.OpenPrice) * myOrder.Quantity) - myOrder.Fee;
            myOrder.IsClosed = 1;
            myOrder.Type = closeType;
            myOrder.CloseDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            _appDbContext.SaveChanges();
        }

        private double CalculateAvragePrice(BinanceOrder myOrder)
        {
            var executedAmount = myOrder.fills.Sum(p => double.Parse(p.price) * double.Parse(p.qty));
            var executedQty = myOrder.fills.Sum(p => double.Parse(p.qty));
            return executedAmount / executedQty;
        }

        #endregion

        #region Helper

        private void ExportChart(bool doExport)
        {
            if(exportStarted && doExport)
            {
                return;
            }

            if(!exportStarted && doExport)
            {
                exportStarted = true;
                 _hub.Clients.All.SendAsync("exportChart");   ///export chart for all symbol monitorerd
            }

            if(!doExport) {
                exportStarted = false;
            }
        }

        private List<Order> GetActiveOrder()
        {
            return _appDbContext.Order.Where(p => p.IsClosed == 0).ToList();
        }

        private Order GetLastOrder()
        {
            Order lastOrder = _appDbContext.Order.OrderByDescending(p => p.Id).Select(p => p).FirstOrDefault();
            if (lastOrder != null)
            {
                return lastOrder;
            }
            else
            {
                return new Order();
            }
        }

        private List<string> GetSymbolList()
        {
            return _appDbContext.Symbol.Select(p => p.SymbolName).ToList();
        }

        private void SaveHighLow(Candle lastCandle, Order activeOrder)
        {
            if (lastCandle.c > activeOrder.HighPrice)
            {
                activeOrder.HighPrice = lastCandle.c;
            }

            if (lastCandle.c < activeOrder.LowPrice)
            {
                activeOrder.LowPrice = lastCandle.c;
            }

            _appDbContext.Order.Update(activeOrder);
            _appDbContext.SaveChanges();
        }

        private void GetCandles(string symbol)
        {
            //Get data from Binance API
            string apiUrl = $"https://api3.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit=100";
            List<List<double>> coinQuotation = HttpHelper.GetApiData<List<List<double>>>(new Uri(apiUrl));
            List<Candle> candleList = new List<Candle>();
            candleList = AutotradeHelper.CreateCandleList(coinQuotation, symbol);
            candleMatrice.Add(candleList);
        }

        #endregion

        #region Binance

        [HttpGet("[action]")]
        public void TestBinanceBuy()
        {
            Globals.swallowOneOrder = true;
            //Symbol + USDT amount
            //var ttt = BinanceHelper.OrderStatus("ETHUSDT", 123);
            // BinanceHelper.BuyMarket("ETHUSDT", 100);
        }
        #endregion
    }
}
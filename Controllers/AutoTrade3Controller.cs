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
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MarginCoin.Service;

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
        private IMLService _mlService;
        private IWatchDog _watchDog;
        private readonly ApplicationDbContext _appDbContext;
        private ILogger _logger;
        private List<List<Candle>> candleMatrice = new List<List<Candle>>();
        private List<MarketStream> marketStreamOnSpot = new List<MarketStream>();
        private List<string> mySymbolList = new List<string>();
        int nbrUp = 0;
        int nbrDown = 0;

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////------------SETTINGS----------/////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        private readonly string interval = "30m";   //1h seem to give better result
        private readonly int numberPreviousCandle = 1;
        private readonly double stopLose = 2;
        private readonly double takeProfit = 1.3;
        //max trade that can be open
        private readonly int maxOpenTrade = 3;
        //Max amount to invest for each trade
        private readonly int quoteOrderQty = 3000;
        //Select short list of symbol or full list(on test server on 6 symbols allowed)
        private readonly bool fullSymbolList = true;

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        ///////////////////////////////-----------Constructor----------/////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        public AutoTrade3Controller(
            IHubContext<SignalRHub> hub,
            [FromServices] ApplicationDbContext appDbContext,
            ILogger<AutoTrade3Controller> logger,
            IBinanceService binanceService,
            IMLService mLService,
            IWatchDog watchDog)
        {
            _hub = hub;
            _binanceService = binanceService;
            _appDbContext = appDbContext;
            _logger = logger;
            _mlService = mLService;
            _watchDog = watchDog;

            //For cross reference between controllers
            Globals.fullSymbolList = fullSymbolList;

            //Get the list of symbol to trade from DB
            mySymbolList = GetSymbolList();
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////////-----------HTTP REQUEST-------/////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        #region http API request

        [HttpGet("[action]")]
        public async Task<string> MonitorMarket()
        {
            _logger.LogWarning("Start trading market...");
            _mlService.CleanImageFolder();

            if (!_watchDog.IsWebsocketSpotDown)
            {
                _mlService.ActivateML();
                _watchDog.CallMethod(RestartWebSocket);

                //open a webSocket for each symbol in my list
                foreach (var symbol in mySymbolList)
                {
                    GetCandles(symbol);
                    OpenWebSocketOnSymbol(symbol);
                }

                //Open websoket on Spot
                await OpenWebSocketOnSpot();
            }
            else
            {
                await OpenWebSocketOnSpot();
            }

            _watchDog.Clear();
            return "";
        }

        [HttpGet("[action]/{orderId}/{lastPrice}")]
        public string CloseTrade(int orderId, double lastPrice)
        {
            Order myOrder = _appDbContext.Order.Where(p => p.Id == orderId).Select(p => p).FirstOrDefault();
            if (myOrder != null)
            {
                if (Globals.onAir)
                {
                    Console.WriteLine("Close trade : by user");
                    Sell(orderId, "by user");
                }
                else
                {
                    Console.WriteLine("Close fake trade : by user");
                    SellFack(orderId, "by user", lastPrice);
                }
            }
            return "";
        }

        [HttpGet("[action]")]
        public string UpdateML()
        {
            _mlService.UpdateML();
            return "";
        }

        [HttpGet("[action]")]
        public List<string> GetSymbolList()
        {
            if (fullSymbolList)
            {
                return _appDbContext.Symbol.Where(p => p.IsOnProd != 0).Select(p => p.SymbolName).ToList();
            }
            else
            {
                return _appDbContext.Symbol.Where(p => p.IsOnTest != 0).Select(p => p.SymbolName).ToList();
            }
        }

        #endregion

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        //////////////////////////-----------WEB SOCKETS SUBSCRIPTION----------/////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        #region Web Sockets

        public void RestartWebSocket()
        {
            _logger.LogWarning($"Restart WebSocket on SPOT");
            _watchDog.IsWebsocketSpotDown = true;
            _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.WebSocketStopped.ToString());
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
                }
                else
                {
                    Console.WriteLine($"New candle save : {stream.k.s}");
                    Globals.onHold.Remove(stream.k.s);
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
                async (data) =>
                {
                    dataResult += data;
                    //Concatain until binance JSON is completed
                    if (dataResult.Contains("}]"))
                    {
                        if (dataResult.Length > (dataResult.IndexOf("]") + 1))
                        {
                            dataResult = dataResult.Remove(dataResult.IndexOf("]") + 1);
                        }

                        List<MarketStream> marketStreamList = Helper.deserializeHelper<List<MarketStream>>(dataResult);
                        dataResult = "";  //we clean it immediatly to avoid a bug on new data coming

                        marketStreamList = marketStreamList.Where(p => p.s.Contains("USDT")).Select(p => p).OrderByDescending(p => p.P).ToList();
                        AutotradeHelper.BufferMarketStream(marketStreamList, ref marketStreamOnSpot);

                        nbrUp = marketStreamOnSpot.Where(pred => pred.P >= 0).Count();
                        nbrDown = marketStreamOnSpot.Where(pred => pred.P < 0).Count();

                        _watchDog.Clear();

                        if (Globals.isTradingOpen)
                        {
                            await ProcessMarketMatrice();
                        }
                    }
                    //return Task.CompletedTask;

                }, CancellationToken.None);

            await ws1.ConnectAsync(CancellationToken.None);
            string message = await onlyOneMessage.Task;
            await ws1.DisconnectAsync(CancellationToken.None);

            return "";
        }

        #endregion

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////////-----------ALGORYTME----------/////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        #region Algo

        private async Task<string> ProcessMarketMatrice()
        {
            try
            {
                //Get active orders
                List<Order> activeOrderList = GetActiveOrder();

                //Check keep open / close 
                activeOrderList.Where(p => CheckTrade(p, candleMatrice)).ToList();

                //Filter marketStreamOnSpot list to get only the one from mySymbolList
                marketStreamOnSpot = marketStreamOnSpot.Where(p => mySymbolList.Any(p1 => p1 == p.s)).OrderByDescending(p => p.P).ToList();

                //Send last data to frontend
                await _hub.Clients.All.SendAsync("trading", JsonSerializer.Serialize(marketStreamOnSpot));

                if (activeOrderList.Count == maxOpenTrade) return "";

                //check candle (indicators, etc, not on hold ) and invest
                for (int i = 0; i < maxOpenTrade - activeOrderList.Count; i++)
                {
                    //read symbol name
                    var symbol = marketStreamOnSpot[i].s;

                    //if there is already a pending order for this symbol we exit
                    if (activeOrderList.Where(p => p.Symbol == symbol).Select(p => p).ToList().Count != 0)
                    {
                        continue;
                    }

                    //Find the line that coorespond to the symbol in the Matrice
                    List<Candle> symbolCandle = candleMatrice.Where(p => p.First().s == symbol).FirstOrDefault();

                    //For debugging, comment out
                    if (Globals.swallowOneOrder)
                    {
                        Globals.swallowOneOrder = false;
                        if (Globals.onAir)
                        {
                            Console.WriteLine($"Open trade on {symbol}");
                            Buy(marketStreamOnSpot[i], symbolCandle);
                        }
                        else
                        {
                            Console.WriteLine($"Open Fack trade on {symbol}");
                            BuyFack(marketStreamOnSpot[i], symbolCandle);
                        }
                    }

                    if (_mlService.MLPredList.Where(p => p.Symbol == symbol).Select(p => p.PredictedLabel).FirstOrDefault() == "up"
                        && _mlService.MLPredList.Where(p => p.Symbol == symbol).Select(p => p.Score[1]).FirstOrDefault() >= 0.60)
                    {
                        if ((CheckCandle(numberPreviousCandle, marketStreamOnSpot[i], symbolCandle) && !Globals.onHold.FirstOrDefault(p => p.Key == symbol).Value))
                        {

                            if (!Globals.onHold.ContainsKey(symbol)) Globals.onHold.Add(symbol, true);  //to avoid multi buy
                            if (Globals.onAir)
                            {
                                Console.WriteLine($"Open trade on {symbol}");
                                Buy(marketStreamOnSpot[i], symbolCandle);
                            }
                            else
                            {
                                Console.WriteLine($"Open Fack trade on {symbol}");
                                BuyFack(marketStreamOnSpot[i], symbolCandle);
                            }
                        }
                    }
                }
                return "";
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, " ProcessMarketMatrice");
                return "";
            }
        }

        private bool CheckCandle(int numberCandle, MarketStream symbolSpot, List<Candle> symbolCandle)
        {   //Question : why using the marketFirstCoin parameter as we have the last value in the last candle in the list

            if (symbolCandle.Count < 2) return false;

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
                    // continue;
                    return true;
                }
                else
                {
                    return false;
                }
            }

            //3 - If MACD 100 don't buy
            // if (symbolCandle.Last().Macd == 100)
            // {
            //     return false;
            // }

            //4 - RSI should be lower than 72 or RSI lower 80 if we already trade the coin
            //if (symbolCandle.Last().Rsi < 56)
            //if (symbolCandle.Last().Rsi < 70)
            //{
            //    return true;
            //}
            // if (symbolCandle.Last().Rsi > 35)
            //{
            //    return true;
            //}
           
            return false;
        }

        private bool CheckTrade(Order activeOrder, List<List<Candle>> myCandleMatrice)
        {
            //iteration the matrice to find the line for the symbol of the active order
            List<Candle> symbolCandle = myCandleMatrice.Where(p => p.First().s == activeOrder.Symbol).FirstOrDefault();

            double lastPrice = symbolCandle.Select(p => p.c).LastOrDefault();
            double highPrice = symbolCandle.Select(p => p.h).LastOrDefault();
            Candle lastCandle = symbolCandle.Select(p => p).LastOrDefault();

            SaveHighLow(lastCandle, activeOrder);

            //Check stop lose
            if (lastPrice <= (activeOrder.StopLose))
            {
                if (Globals.onAir)
                {
                    Console.WriteLine("Close trade : stop lose ");
                    Sell(activeOrder.Id, "stop lose");
                }
                else
                {
                    Console.WriteLine("Close fake trade : stop lose ");
                    SellFack(activeOrder.Id, "stop lose", lastPrice);
                }
            }

            //After +2% we use a take profit
            if (lastPrice > activeOrder.OpenPrice * 1.02)
            {
                if (lastPrice <= (activeOrder.HighPrice * (1 - (activeOrder.TakeProfit / 100))))
                {
                    if (Globals.onAir)
                    {
                        Console.WriteLine("Close trade : take profit");
                        Sell(activeOrder.Id, "Take profit");
                    }
                    else
                    {
                        Console.WriteLine("Close fake trade : take profit");
                        SellFack(activeOrder.Id, "Take profit", lastPrice);
                    }
                }
            }

            //AI close
            if (_mlService.MLPredList.Where(p => p.Symbol == activeOrder.Symbol).Select(p => p.PredictedLabel).FirstOrDefault() == "down"
             && _mlService.MLPredList.Where(p => p.Symbol == activeOrder.Symbol).Select(p => p.Score[0]).FirstOrDefault() >= 0.85)
            {
                if (Globals.onAir)
                {
                    Console.WriteLine("Close trade : AI take profit ");
                    Sell(activeOrder.Id, "AI sold");
                }
                else
                {
                    Console.WriteLine("Close fake trade : AI take profit ");
                    SellFack(activeOrder.Id, "AI sold", lastPrice);
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

        #endregion

        #region Buy / Sell

        private async void Buy(MarketStream symbolSpot, List<Candle> symbolCandleList)
        {
            System.Net.HttpStatusCode httpStatusCode = System.Net.HttpStatusCode.NoContent;
            BinanceOrder myBinanceOrder = _binanceService.BuyMarket(symbolSpot.s, quoteOrderQty, ref httpStatusCode);

            //if order not executed we try divide amount by 2 and 3
            if (httpStatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                myBinanceOrder = _binanceService.BuyMarket(symbolSpot.s, quoteOrderQty / 2, ref httpStatusCode);
                if (httpStatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    myBinanceOrder = _binanceService.BuyMarket(symbolSpot.s, quoteOrderQty / 3, ref httpStatusCode);
                    if (httpStatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        _logger.LogWarning($"Call {MyEnum.BinanceApiCall.BuyMarket} {symbolSpot.s} Locked");
                        return;
                    }
                }
            }

            if (myBinanceOrder == null)
            {
                Globals.onHold.Remove(symbolSpot.s);
                return;
            }

            if (myBinanceOrder.status == "EXPIRED")
            {
                await _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.BinanceSellOrderExpired.ToString());
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
            myOrder.Quantity = Helper.ToDouble(binanceOrder.executedQty);
            myOrder.IsClosed = 0;
            myOrder.Fee = Globals.isProd ? binanceOrder.fills.Sum(p => Helper.ToDouble(p.commission)) : Math.Round((CalculateAvragePrice(binanceOrder) * Helper.ToDouble(binanceOrder.executedQty)) / 100) * 0.1;
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
            var executedAmount = myOrder.fills.Sum(p => Helper.ToDouble(p.price) * Helper.ToDouble(p.qty));
            var executedQty = myOrder.fills.Sum(p => Helper.ToDouble(p.qty));
            return executedAmount / executedQty;
        }

        #endregion

        #region Helper

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

        #region Debug

        private async void BuyFack(MarketStream symbolSpot, List<Candle> symbolCandleList)
        {

            BinanceOrder fackBinanceOrder = new BinanceOrder();
            fackBinanceOrder.symbol = symbolSpot.s;
            fackBinanceOrder.executedQty = (quoteOrderQty / symbolSpot.c).ToString();
            fackBinanceOrder.cummulativeQuoteQty = quoteOrderQty.ToString();
            fackBinanceOrder.status = "FACK!";
            fackBinanceOrder.price = symbolSpot.c.ToString();
            fackBinanceOrder.side = "BUY";
            fills theOrderDetail = new fills();
            theOrderDetail.price = symbolSpot.c.ToString();
            theOrderDetail.qty = (quoteOrderQty / symbolSpot.c).ToString();
            fackBinanceOrder.fills = new List<fills>();
            fackBinanceOrder.fills.Add(theOrderDetail);

            SaveTrade(symbolSpot, symbolCandleList, fackBinanceOrder);
            Globals.onHold.Remove(symbolSpot.s);
            await _hub.Clients.All.SendAsync("newPendingOrder", JsonSerializer.Serialize(fackBinanceOrder));
            await Task.Delay(500);
            await _hub.Clients.All.SendAsync("newOrder");
        }

        private async void SellFack(double id, string closeType, double lastPrice)
        {
            Order myOrder = _appDbContext.Order.Where(p => p.Id == id).Select(p => p).FirstOrDefault();

            BinanceOrder fackBinanceOrder = new BinanceOrder();
            fackBinanceOrder.symbol = myOrder.Symbol;
            fackBinanceOrder.executedQty = (quoteOrderQty / lastPrice).ToString();
            fackBinanceOrder.cummulativeQuoteQty = quoteOrderQty.ToString();
            fackBinanceOrder.status = "FACK!";
            fackBinanceOrder.price = lastPrice.ToString();
            fackBinanceOrder.side = "SELL";
            fills theOrderDetail = new fills();
            theOrderDetail.price = lastPrice.ToString();
            theOrderDetail.qty = (quoteOrderQty / lastPrice).ToString();
            fackBinanceOrder.fills = new List<fills>();
            fackBinanceOrder.fills.Add(theOrderDetail);

            CloseTrade(id, closeType, fackBinanceOrder);
            await Task.Delay(500);
            await _hub.Clients.All.SendAsync("sellOrderFilled", JsonSerializer.Serialize(fackBinanceOrder));

        }

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
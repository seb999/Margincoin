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
    public class AutoTrade3Controller : ControllerBase
    {

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////------------Global varibles----------//////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        private IHubContext<SignalRHub> _hub;
        private readonly ApplicationDbContext _appDbContext;
        private List<List<Candle>> candleMatrice = new List<List<Candle>>();
        private List<Candle> candleListMACD = new List<Candle>();
        private List<MarketStream> marketStreamOnSpot = new List<MarketStream>();
        int nbrUp = 0;
        int nbrDown = 0;

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////------------SETTINGS----------/////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        string interval = "1h";   //1h seem to give better result

        int numberPreviousCandle = 2;

        double stopLose = 0.5;

        double takeProfit = 0.5;

        //move stop lose to buy price when current price raise over:1.2%
        double secureNoLose = 1.016;
        //max trade that can be open
        int maxOpenTrade = 6;
        List<string> mySymbolList = new List<string>();

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        ///////////////////////////////-----------Constructor----------/////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        public AutoTrade3Controller(IHubContext<SignalRHub> hub, [FromServices] ApplicationDbContext appDbContext)
        {
            _hub = hub;
            _appDbContext = appDbContext;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////////-----------ALGORYTME----------/////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////

        [HttpGet("[action]")]
        public async Task<string> MonitorMarket()
        {
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

        [HttpGet("[action]/{orderId}/{lastPrice}")]
        public async Task<string> CloseTrade(int orderId)
        {
            Order myOrder = _appDbContext.Order.Where(p => p.Id == orderId).Select(p => p).FirstOrDefault();
            if (myOrder != null)
            {
                CloseTrade(orderId, "by user");

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
                }
                else
                {
                    Console.WriteLine($"New candle save : {stream.k.s}");
                    Globals.symbolOnHold.Remove(stream.k.s);
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
                    activeOrderList.Where(p => CheckStopLose(p)).ToList();
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

                    if (CheckCandle(numberPreviousCandle, marketStreamOnSpot[i], symbolCandle) && !symbolCandle.Last().IsOnHold && !Globals.symbolOnHold.FirstOrDefault(p => p.Key == symbol).Value)
                    {
                        if(!Globals.isTradingOpen)
                        {
                            Console.WriteLine($"Open trade on {symbol}");
                            Buy(marketStreamOnSpot[i], symbolCandle);
                        }
                        else
                        {
                            Console.WriteLine($"Trading closed by user");
                        }
                       
                    }
                }
            }
            catch (System.Exception)
            {
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

            //0 - Don't trade if only 14% coins are up over the last 24h
            if ((double)nbrUp < 40)
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

        private bool CheckStopLose(Order activeOrder)
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

            if (lastPrice <= activeOrder.OpenPrice * (1 - (this.stopLose / 100)))
            {
                Console.WriteLine("Close trade : stop lose ");

                candleMatrice[symbolCandleIndex].Last().IsOnHold = true;
                Sell(activeOrder.Id, "by user");
            }
            if (lastPrice > activeOrder.OpenPrice)
            {
                if (lastPrice <= (activeOrder.HighPrice * (1 - (this.takeProfit / 100))))
                {
                    Console.WriteLine("Close trade : take profit ");
                    candleMatrice[symbolCandleIndex].Last().IsOnHold = true;
                    Sell(activeOrder.Id, "Take profit");
                }
            }

            SaveHighLow(lastCandle, activeOrder);

            UpdateStopLose(lastPrice, activeOrder);

            UpdateTakeProfit(lastPrice, activeOrder);

            return true;
        }

        private void UpdateTakeProfit(double currentPrice, Order activeOrder)
        {
            OrderTemplate orderTemplate = GetOrderTemplate();
            double pourcent = ((currentPrice - activeOrder.OpenPrice) / activeOrder.OpenPrice) * 100;

            if (pourcent <= 0) return;

            if (2 < pourcent && pourcent <= 3)
            {
                activeOrder.TakeProfit = orderTemplate.TakeProfit - 0.4;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }

            if (3 < pourcent && pourcent <= 4)
            {
                activeOrder.TakeProfit = orderTemplate.TakeProfit - 0.5;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }
            if (4 < pourcent && pourcent <= 5)
            {
                activeOrder.TakeProfit = orderTemplate.TakeProfit - 0.6;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }
            if (5 < pourcent)
            {
                activeOrder.TakeProfit = orderTemplate.TakeProfit - 0.7;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }
        }

        private void UpdateStopLose(double lastPrice, Order activeOrder)
        {
            if (lastPrice >= activeOrder.OpenPrice * secureNoLose)
            {
                activeOrder.StopLose = activeOrder.OpenPrice;
                _appDbContext.Order.Update(activeOrder);
                _appDbContext.SaveChanges();
            }
        }


        #region Buy / Sell

        private async void Buy(MarketStream symbolSpot, List<Candle> symbolCandleList)
        {
            //What we want to buy ?
            OrderTemplate orderTemplate = GetOrderTemplate();
            double quoteQty = orderTemplate.Amount;

            //What type of order we want ?
            BinanceOrder myBinanceOrder = BinanceHelper.BuyMarket(symbolSpot.s, quoteQty);
            if (myBinanceOrder == null) return;

            //Save here binance order result in db
            SaveTrade(symbolSpot, symbolCandleList, myBinanceOrder.orderId);
            await _hub.Clients.All.SendAsync("newPendingOrder", null, JsonSerializer.Serialize(myBinanceOrder));

            if (BinanceHelper.OrderStatus(myBinanceOrder.symbol, myBinanceOrder.orderId).status == "Filled")
            {
                UpdateTrade(myBinanceOrder, false);
                await Task.Delay(500);
                await _hub.Clients.All.SendAsync("newOrder");
            }
        }

        private async void Sell(double id, string closeType)
        {
            Order myOrder = _appDbContext.Order.Where(p => p.Id == id).Select(p => p).FirstOrDefault();

            //What type of order we want ?
            BinanceOrder myBinanceOrder = BinanceHelper.SellMarket(myOrder.Symbol, myOrder.Quantity);
            if (myBinanceOrder == null) return;

            CloseTrade(id, closeType);

            if (BinanceHelper.OrderStatus(myBinanceOrder.symbol, myBinanceOrder.orderId).status == "Filled")
            {
                UpdateTrade(myBinanceOrder, true);
                await Task.Delay(500);
                await _hub.Clients.All.SendAsync("newOrder");
            }
        }

        private void SaveTrade(MarketStream symbolSpot, List<Candle> symbolCandle, double binanceOrderId)
        {
            //Console.Beep();
            Console.WriteLine("Open trade");

            List<ModelOutput> prediction = AIHelper.GetPrediction(symbolCandle);

            //1-read from db template
            OrderTemplate orderTemplate = GetOrderTemplate();
            double quantity = orderTemplate.Amount / symbolSpot.c;

            Order myOrder = new Order();
            myOrder.OpenDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            myOrder.OpenPrice = symbolSpot.c;
            myOrder.HighPrice = symbolSpot.c;
            myOrder.LowPrice = symbolSpot.c;
            myOrder.Volume = symbolSpot.v;
            myOrder.TakeProfit = orderTemplate.TakeProfit;
            myOrder.StopLose = symbolSpot.c * (1 - (orderTemplate.StopLose / 100));
            myOrder.Quantity = quantity;
            myOrder.IsClosed = 0;
            myOrder.Fee = Math.Round((symbolSpot.c * quantity) / 100) * 0.1;
            myOrder.Symbol = symbolSpot.s;

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
            myOrder.PredictionLBFGS = prediction[0].Prediction == true ? 1 : 0;
            myOrder.PredictionLDSVM = prediction[1].Prediction == true ? 1 : 0;
            myOrder.PredictionSDA = prediction[2].Prediction == true ? 1 : 0;

            myOrder.Lock = 0;
            myOrder.MarketTrend = $"{nbrUp}|{nbrDown}";
            myOrder.Status = "Pending";
            myOrder.OrderId = binanceOrderId;

            _appDbContext.Order.Add(myOrder);
            _appDbContext.SaveChanges();
        }

        private void UpdateTrade(BinanceOrder myBinanceOrder, bool isClosed)
        {
            Order myOrder = _appDbContext.Order.Where(p => p.OrderId == myBinanceOrder.orderId).Select(p => p).FirstOrDefault();
            myOrder.Status = myBinanceOrder.status;

            if (!isClosed)
            {
                myOrder.OpenPrice = double.Parse(myBinanceOrder.price);
                myOrder.Fee = myBinanceOrder.fills.Sum(P => long.Parse(P.commission));
                myOrder.Quantity = double.Parse(myBinanceOrder.executedQty);
            }
            else
            {
                myOrder.ClosePrice = double.Parse(myBinanceOrder.price);
                myOrder.Fee += myBinanceOrder.fills.Sum(P => long.Parse(P.commission));
                myOrder.Profit = Math.Round((myOrder.ClosePrice - myOrder.OpenPrice) * myOrder.Quantity) - myOrder.Fee;
                myOrder.IsClosed = 1;
            }

            _appDbContext.Order.Update(myOrder);
            _appDbContext.SaveChanges();
        }

        private void CloseTrade(double orderId, string closeType)
        {
            Order myOrder = _appDbContext.Order.Where(p => p.Id == orderId).Select(p => p).FirstOrDefault();
            Globals.symbolOnHold.Add(myOrder.Symbol, true);
            myOrder.Type = closeType;
            myOrder.CloseDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            _appDbContext.SaveChanges();
        }

        #endregion

        #region Helper

        private OrderTemplate GetOrderTemplate()
        {
            return _appDbContext.OrderTemplate.Where(p => p.IsInactive != 1).Select(p => p).FirstOrDefault();
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
        public List<CryptoAsset> BinanceAsset()
        {
            System.Net.HttpStatusCode httpStatusCode = System.Net.HttpStatusCode.NoContent;

            //My asset and quantity available from Binance wallet
            List<CryptoAsset> myAsset = BinanceHelper.Asset(ref httpStatusCode);

            if (httpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.ApiAccessFaulty.ToString());
            }
            if (httpStatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.ApiTooManyRequest.ToString());
            }
            if (httpStatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                _hub.Clients.All.SendAsync(MyEnum.BinanceHttpError.ApiCheckAllowedIP.ToString());
            }

            if (myAsset == null) return null;

            CryptoAsset myUsdtAsset = myAsset.Where(p => p.Asset == "USDT").FirstOrDefault();
            //List of symbol we trade here
            List<string> mySymbol = GetSymbolList();
            //We filter Asset to return only the one we trade
            myAsset = myAsset.Where(p => mySymbol.Any(p1 => p1.Replace("USDT", "") == p.Asset)).OrderByDescending(p => p.BtcValuation).ToList();
            //We re add the USDT at top position
            myAsset.Insert(0, myUsdtAsset);
            return myAsset;
        }

        [HttpGet("[action]")]
        public void TestBinanceBuy()
        {
            //Symbol + USDT amount
            var ttt = BinanceHelper.OrderStatus("ETHUSDT", 123);
            // BinanceHelper.BuyMarket("ETHUSDT", 100);
        }

        #endregion
    }
}
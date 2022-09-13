using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using MarginCoin.Class;
using MarginCoin.Model;

namespace MarginCoin.Misc
{
    public static class AutotradeHelper
    {
        //in construction :  idea is to check the market direction with major coins
         internal static void MarketPerformance(List<MarketStream> marketStreamList)
        {
            int marketPerf = 0;
            foreach (var marketStream in marketStreamList)
            {
                if(marketStream.s == "ETHUSDT" 
                || marketStream.s == "BTCUSDT"
                || marketStream.s == "BTCUSDT"
                || marketStream.s == "BTCUSDT"
                || marketStream.s == "BTCUSDT"
                || marketStream.s == "BTCUSDT")
                {

                }
            }
        }


        //The stream just contain symbol that changed from t-1, we buffer it to get all symbol changed or not changed
        internal static void BufferMarketStream(List<MarketStream> marketStreamList, ref List<MarketStream> buffer)
        {
            foreach (var marketStream in marketStreamList)
            {
                var bufferItem = buffer.Where(p => p.s == marketStream.s).FirstOrDefault();
                if (bufferItem != null)
                {
                    bufferItem.A = marketStream.A;
                    bufferItem.a = marketStream.a;
                    bufferItem.B = marketStream.B;
                    bufferItem.c = marketStream.c;
                    bufferItem.C = marketStream.C;
                    bufferItem.e = marketStream.e;
                    bufferItem.E = marketStream.E;
                    bufferItem.F = marketStream.F;
                    bufferItem.h = marketStream.h;
                    bufferItem.L = marketStream.L;
                    bufferItem.l = marketStream.l;
                    bufferItem.n = marketStream.n;
                    bufferItem.O = marketStream.O;
                    bufferItem.o = marketStream.o;
                    bufferItem.P = marketStream.P;
                    bufferItem.p = marketStream.p;
                    bufferItem.Q = marketStream.Q;
                    bufferItem.q = marketStream.q;
                    bufferItem.s = marketStream.s;
                    bufferItem.v = marketStream.v;
                    bufferItem.w = marketStream.w;
                    bufferItem.x = marketStream.x;
                }
                else
                    buffer.Add(marketStream);
            }

            buffer = buffer.Select(p => p).OrderByDescending(p => p.P).ToList();

        }

        internal static void GetMarketCandles(List<MarketStream> marketStreamList, ref List<List<Candle>> marketCandles, string interval)
        {
           
            //for(int i =0;i<3;i++)
             foreach (var marketStream in marketStreamList)
            {
                string apiUrl = $"https://api3.binance.com/api/v3/klines?symbol={marketStream.s}&interval={interval}&limit=50";
                List<List<double>> coinQuotation = HttpHelper.GetApiData<List<List<double>>>(new Uri(apiUrl));

                List<Candle> mySymbolCandles = new List<Candle>();
                foreach (var item in coinQuotation)
                {
                    Candle newCandle = new Candle()
                    {
                        s = marketStream.s,
                        T = item[0],
                        o = item[1],
                        h = item[2],
                        l = item[3],
                        c = item[4],
                        v = item[5],
                        t = item[6],
                    };
                    mySymbolCandles.Add(newCandle);
                }
                TradeIndicator.CalculateIndicator(ref mySymbolCandles);

                marketCandles.Add(mySymbolCandles);
            }
        }

        internal static void DisplayStatus(Order activeOrder, List<MarketStream> marketStreamList)
        {
            if (activeOrder != null)
            {
                double currentPrice = marketStreamList.Where(p => p.s == activeOrder.Symbol).Select(p => p.c).FirstOrDefault();
                Console.WriteLine(DateTime.Now + " - Trading - Profit : " + Math.Round(((currentPrice - activeOrder.OpenPrice) * activeOrder.Quantity) - activeOrder.Fee));
            }
            else
            {
                Console.WriteLine(DateTime.Now + " - Trading - No active order");
            }
        }

        internal static bool DataQualityCheck(List<MarketStream> marketStreamList)
        {
            if (marketStreamList.Last().c != 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        internal static string CandleColor(Candle candle)
        {
            if (candle.c > candle.o) return "green";
            if (candle.c < candle.o) return "red";
            return "";
        }
    }
}
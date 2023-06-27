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
    public static class TradeHelper
    {

        public static MacdSlope CalculateMacdSlope(List<Candle> symbolCandles, string tradingInterval)
        {
            //We calculate how many candle for 1Hours
            var interval = TradeHelper.NumberCandleInInterval(tradingInterval, 1);

            //Calculate the slope for 4 differente point
            var coefficient1 = (symbolCandles[symbolCandles.Count - 1].MacdHist - symbolCandles[symbolCandles.Count - (1 + interval)].MacdHist) / interval;
            var coefficient2 = (symbolCandles[symbolCandles.Count - 2].MacdHist - symbolCandles[symbolCandles.Count - (2 + interval)].MacdHist) / interval;
            var coefficient3 = (symbolCandles[symbolCandles.Count - 3].MacdHist - symbolCandles[symbolCandles.Count - (3 + interval)].MacdHist) / interval;
            var coefficient4 = (symbolCandles[symbolCandles.Count - 4].MacdHist - symbolCandles[symbolCandles.Count - (4 + interval)].MacdHist) / interval;


            MacdSlope mySlope = new MacdSlope();
            mySlope.Slope = (coefficient1 + coefficient2 + coefficient3 + coefficient4) / 4;
            mySlope.P2 = new Point() { x = symbolCandles[symbolCandles.Count - 1].t, y = symbolCandles[symbolCandles.Count - 1].MacdHist };
            mySlope.P1 = new Point() { x = symbolCandles[symbolCandles.Count - 5].t, y = symbolCandles[symbolCandles.Count - 5].MacdHist };

            return mySlope;
        }

        /// <summary>
        /// Return change in % from a list of candle
        /// Taking the lower for a upper trend or Higher for down trend
        /// </summary>
        /// <param name="last">The last value c</param>
        /// <param name="candleMatrice">The list of candle</param>
        /// <returns></returns>
        public static double CalculPourcentChange(double last, List<Candle> candleMatrice, string tradingInterval, int backTimelineHours)
        {
            try
            {
                var backTimeCandle = TradeHelper.NumberCandleInInterval(tradingInterval, backTimelineHours);
                return ((last - candleMatrice[candleMatrice.Count - backTimeCandle].c) / last) * 100;
            }
            catch (System.Exception ex)
            {
                return 0;
            }
        }

        public static double CalculPourcentChange(List<Candle> candleMatrice, int prevCandleCount)
        {
            return ((candleMatrice.Last().c - candleMatrice[candleMatrice.Count - prevCandleCount].c) / candleMatrice[candleMatrice.Count - prevCandleCount].c) * 100;
        }

        /// <summary>
        /// Return how many candle back we have to go to calculate P (pourcentage change)
        /// </summary>
        /// <param name="tradingInterval">The interval for trading ex: 1h, 30m, 15m</param>
        /// <param name="interval">The number of hours back we want to use</param>
        /// <returns></returns>
        private static int NumberCandleInInterval(string tradingInterval, int interval)
        {
            switch (tradingInterval.Substring(tradingInterval.Length - 1))
            {
                case "h":
                    return int.Parse(tradingInterval.Remove(1));
                case "m":
                    return interval * 60 / int.Parse(tradingInterval.Remove(tradingInterval.Length - 1));
                default:
                    return 0;
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

        public static List<Candle> CreateCandleList(List<List<double>> coinQuotation, string symbol)
        {
            List<Candle> candleList = new List<Candle>();
            foreach (var item in coinQuotation)
            {
                Candle newCandle = new Candle()
                {
                    s = symbol,
                    T = item[0],
                    o = item[1],
                    h = item[2],
                    l = item[3],
                    c = item[4],
                    v = item[5],
                    t = item[6],
                    id = Guid.NewGuid().ToString(),
                    IsOnHold = false,
                };
                candleList.Add(newCandle);
            }
            TradeIndicator.CalculateIndicator(ref candleList);
            return candleList;
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
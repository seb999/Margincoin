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
        /// <summary>
        /// Calculate trend score based on multiple technical indicators
        /// Score ranges from -5 (strong bearish) to +5 (strong bullish)
        /// </summary>
        /// <param name="candles">List of candles with calculated indicators</param>
        /// <param name="useWeighted">If true, gives more weight to trend indicators (EMA, MACD)</param>
        /// <returns>Integer score representing trend strength and direction</returns>
        public static int CalculateTrendScore(List<Candle> candles, bool useWeighted = false)
        {
            if (candles == null || candles.Count < 2)
                return 0;

            var current = candles[^1]; // Latest candle
            var previous = candles[^2]; // Previous candle

            int score = 0;

            if (useWeighted)
            {
                // Weighted scoring - trend indicators get more weight

                // Trend indicators (weight: 2)
                if (current.c > current.Ema) score += 2; else if (current.c < current.Ema) score -= 2;
                if (current.Macd > current.MacdSign) score += 2; else if (current.Macd < current.MacdSign) score -= 2;

                // Momentum indicators (weight: 1)
                if (current.MacdHist > previous.MacdHist) score += 1; else if (current.MacdHist < previous.MacdHist) score -= 1;
                if (current.Rsi > 50) score += 1; else if (current.Rsi < 50) score -= 1;
                if (current.Rsi > previous.Rsi) score += 1; else if (current.Rsi < previous.Rsi) score -= 1;
            }
            else
            {
                // Equal weight scoring - all indicators count the same

                // Bullish signals (+1 each)
                if (current.c > current.Ema) score++;
                if (current.Macd > current.MacdSign) score++;
                if (current.MacdHist > previous.MacdHist) score++;
                if (current.Rsi > 50) score++;
                if (current.Rsi > previous.Rsi) score++;

                // Bearish signals (-1 each)
                if (current.c < current.Ema) score--;
                if (current.Macd < current.MacdSign) score--;
                if (current.MacdHist < previous.MacdHist) score--;
                if (current.Rsi < 50) score--;
                if (current.Rsi < previous.Rsi) score--;
            }

            return score;
        }

        /// <summary>
        /// Get trend direction as string based on trend score
        /// </summary>
        /// <param name="candles">List of candles with calculated indicators</param>
        /// <param name="useWeighted">If true, uses weighted scoring</param>
        /// <returns>"UP", "DOWN", or "SIDEWAYS"</returns>
        public static string GetTrendDirection(List<Candle> candles, bool useWeighted = false)
        {
            var score = CalculateTrendScore(candles, useWeighted);

            // For weighted scoring, thresholds are higher (max score is ~9)
            var threshold = useWeighted ? 5 : 3;

            if (score >= threshold) return "UP";
            if (score <= -threshold) return "DOWN";
            return "SIDEWAYS";
        }

        public static MacdSlope CalculateMacdSlope(List<Candle> symbolCandles, string tradingInterval)
        {
            var interval = NumberCandleInInterval(tradingInterval, 1);

            // Validate inputs - need at least 4 + interval candles, and interval must not be zero
            if (symbolCandles == null || symbolCandles.Count < (4 + interval) || interval == 0)
            {
                return new MacdSlope { Slope = 0, P1 = new Point(), P2 = new Point() };
            }

            try
            {
                // Calculate the slope for 4 different points using index from end operator
                var coefficient1 = (symbolCandles[^1].MacdHist - symbolCandles[^(1 + interval)].MacdHist) / interval;
                var coefficient2 = (symbolCandles[^2].MacdHist - symbolCandles[^(2 + interval)].MacdHist) / interval;
                var coefficient3 = (symbolCandles[^3].MacdHist - symbolCandles[^(3 + interval)].MacdHist) / interval;
                var coefficient4 = (symbolCandles[^4].MacdHist - symbolCandles[^(4 + interval)].MacdHist) / interval;

                return new MacdSlope
                {
                    Slope = (coefficient1 + coefficient2 + coefficient3 + coefficient4) / 4,
                    P2 = new Point { x = symbolCandles[^1].t, y = symbolCandles[^1].MacdHist },
                    P1 = new Point { x = symbolCandles[^5].t, y = symbolCandles[^5].MacdHist }
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MACD slope calculation failed: {ex.Message}");
                throw;
            }
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
                return (last - candleMatrice[candleMatrice.Count - backTimeCandle].c) / last * 100;
            }
            catch (System.Exception ex)
            {
                return -1;
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
            TradeIndicator.CalculateIndicator(candleList);
            return candleList;
        }

        internal static string CandleColor(Candle candle)
        {
            if (candle.c > candle.o) return "green";
            if (candle.c < candle.o) return "red";
            return "";
        }

        public static double CalculateAvragePrice(BinanceOrder myOrder)
        {
            if (myOrder.fills == null || myOrder.fills.Count == 0)
            {
                return Helper.ToDouble(myOrder.price);
            }

            double executedAmount = myOrder.fills
                .Sum(fill => Helper.ToDouble(fill.price) * Helper.ToDouble(fill.qty));
            double executedQty = myOrder.fills
                .Sum(fill => Helper.ToDouble(fill.qty));
            return executedAmount / executedQty;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using MarginCoin.Class;
using TicTacTec.TA.Library;

namespace MarginCoin.Misc
{
    public static class TradeIndicator
    {
        public static void CalculateIndicator<T>(List<T> quotationList) where T : Candle
        {
            if (quotationList == null || quotationList.Count < 50) return;

            try
            {
                var dataC = quotationList.Select(p => p.c).ToArray();
                var dataH = quotationList.Select(p => p.h).ToArray();
                var dataL = quotationList.Select(p => p.l).ToArray();

                int beginIndex;
                int outNBElements;
                double[] rsiValues = new double[dataC.Length];
                double[] emaValues = new double[dataC.Length];
                double[] outMACD = new double[dataC.Length];
                double[] outMACDSignal = new double[dataC.Length];
                double[] outMACDHist = new double[dataC.Length];
                double[] stochSlowKValues = new double[dataC.Length];
                double[] stochSlowDValues = new double[dataC.Length];

                //Calculate RSI
                var statusRsi = Core.Rsi(0, dataC.Length - 1, dataC, 14, out beginIndex, out outNBElements, rsiValues);
                if (statusRsi == Core.RetCode.Success && outNBElements > 0)
                {
                    for (int i = 0; i < Math.Min(outNBElements, quotationList.Count - 14); i++)
                    {
                        quotationList[i + 14].Rsi = rsiValues[i];
                    }
                }

                //Calculate MACD
                var statusMacd = Core.Macd(0, dataC.Length - 1, dataC, 12, 26, 9, out beginIndex, out outNBElements, outMACD, outMACDSignal, outMACDHist);
                if (statusMacd == Core.RetCode.Success && outNBElements > 0)
                {
                    var macdMax = outMACD.Max();
                    var macdMin = outMACD.Min();
                    var macdSignMax = outMACDSignal.Max();
                    var macdSignMin = outMACDSignal.Min();

                    var macdRange = macdMax - macdMin;
                    var macdSignRange = macdSignMax - macdSignMin;

                    for (int i = 0; i < Math.Min(outNBElements, quotationList.Count - 33); i++)
                    {
                        //MACD normalize - avoid division by zero
                        quotationList[i + 33].Macd = macdRange > 0
                            ? ((outMACD[i] - macdMin) / macdRange) * 100
                            : 50;
                        quotationList[i + 33].MacdSign = macdSignRange > 0
                            ? ((outMACDSignal[i] - macdSignMin) / macdSignRange) * 100
                            : 50;
                        quotationList[i + 33].MacdHist = quotationList[i + 33].Macd - quotationList[i + 33].MacdSign;
                    }
                }

                //Calculate EMA50
                var statusEma = Core.Ema(0, dataC.Length - 1, dataC, 50, out beginIndex, out outNBElements, emaValues);
                if (statusEma == Core.RetCode.Success && outNBElements > 0)
                {
                    var emaMax = emaValues.Max();
                    var emaMin = emaValues.Min();
                    var emaRange = emaMax - emaMin;

                    for (int i = 0; i < Math.Min(outNBElements, quotationList.Count - 49); i++)
                    {
                        quotationList[i + 49].Ema = emaRange > 0
                            ? ((emaValues[i] - emaMin) / emaRange) * 100
                            : 50;
                    }
                }

                //Calculate Stochastic
                var statusStoch = Core.Stoch(0, dataC.Length - 1, dataH, dataL, dataC, 5, 3, 0, 3, 0, out beginIndex, out outNBElements, stochSlowKValues, stochSlowDValues);
                if (statusStoch == Core.RetCode.Success && outNBElements > 0)
                {
                    for (int i = 0; i < Math.Min(outNBElements, quotationList.Count - 8); i++)
                    {
                        quotationList[i + 8].StochSlowD = stochSlowDValues[i];
                        quotationList[i + 8].StochSlowK = stochSlowKValues[i];
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Indicator calculation failed: {ex.Message}");
                throw;
            }
        }

        //A calcultation of the volatility(Average True Range)
        public static double CalculateATR(List<Candle> symbolCandles)
        {
            // Calculate the true range values for each candle
            List<double> trueRanges = new List<double>();
            foreach (Candle candle in symbolCandles)
            {
                double trueRange = Math.Max(
                    candle.h - candle.l,
                    Math.Max(
                        Math.Abs(candle.h - candle.c),
                        Math.Abs(candle.l - candle.c)
                    )
                );
                trueRanges.Add(trueRange);
            }

            // Calculate the average true range using the last 14 candles
            int period = 14;
            double sum = 0;
            for (int i = symbolCandles.Count - period; i < symbolCandles.Count; i++)
            {
                sum += trueRanges[i];
            }
            double atr = sum / period;

            return atr;
        }

        public static void CalculateIndicatorOld<T>(ref List<T> quotationList) where T : Candle
        {
            //Sometime linq crash and say that p is null
            try
            {
                quotationList.Select(p => p.c).ToArray();
            }
            catch (System.Exception)
            {
                return;
            }

            var data = quotationList.Select(p => p.c).ToArray();
            int beginIndex;
            int outNBElements;
            double[] rsiValues = new double[data.Length];
            double[] outMACD = new double[data.Length];
            double[] outMACDSignal = new double[data.Length];
            double[] outMACDHist = new double[data.Length];
            double[] emaValues = new double[data.Length];

            try
            {
                var statusRsi = Core.Rsi(0, data.Length - 1, data, 14, out beginIndex, out outNBElements, rsiValues);
                if (statusRsi == Core.RetCode.Success && outNBElements > 0)
                {
                    for (int i = 0; i < quotationList.Count - 14; i++)
                    {
                        quotationList[i + 14].Rsi = rsiValues[i];
                    }
                }
            }
            catch (System.Exception)
            {
                Console.WriteLine("RSI calulation failled!");
            }
            finally
            {

            }

            //Calculate MACD
            try
            {
                var statusMacd = Core.Macd(0, data.Length - 1, data, 12, 26, 9, out beginIndex, out outNBElements, outMACD, outMACDSignal, outMACDHist);
                if (statusMacd == Core.RetCode.Success && outNBElements > 0)
                {
                    for (int i = 0; i < quotationList.Count - 33; i++)
                    {
                        quotationList[i + 33].Macd = outMACD[i];
                        quotationList[i + 33].MacdHist = outMACDHist[i];
                        quotationList[i + 33].MacdSign = outMACDSignal[i];
                    }
                }
            }
            catch (System.Exception)
            {
                Console.WriteLine("RSI calulation failled!");
            }
            finally
            {

            }


            // //Calculate EMA50
            try
            {
                var statusEma = Core.Ema(0, data.Length - 1, data, 50, out beginIndex, out outNBElements, emaValues);
                if (statusEma == Core.RetCode.Success && outNBElements > 0)
                {
                    for (int i = 0; i < quotationList.Count - 49; i++)
                    {
                        quotationList[i + 49].Ema = emaValues[i];
                    }
                }
            }
            catch (System.Exception)
            {
                Console.WriteLine("EMA50 calulation failled!");
            }
            finally
            {

            }

            //Pivot
            // foreach (var (quote, index) in quotationList.Select((v, i)=>(v, i))) {
            //       if(index == 0)continue;
            //       var PP = ((quotationList[index-1].h + quotationList[index-1].l + quotationList[index-1].c) / 3);
            //         quote.PivotPoint = new PivotPOint()
            //         {
            //             R1 = 2 * PP - quotationList[index-1].l,
            //             S1 = 2 * PP - quotationList[index-1].h,
            //             R2 = PP + quotationList[index-1].h - quotationList[index-1].l,
            //             S2 = (PP - quotationList[index-1].h + quotationList[index-1].l),
            //             R3 = (quotationList[index-1].h + 2 * (PP - quotationList[index-1].l)),
            //             S3 = (quotationList[index-1].l - 2 * (quotationList[index-1].h - PP)),
            //         };
            // }
        }
    }
}
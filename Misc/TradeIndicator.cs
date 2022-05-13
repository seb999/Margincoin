using System;
using System.Collections.Generic;
using System.Linq;
using MarginCoin.Class;
using TicTacTec.TA.Library;

namespace MarginCoin.Misc
{
    public static class TradeIndicator
    {
        public static void CalculateIndicator<T>(ref List<T> quotationList) where T : Candle
        {
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
            // var statusEma = Core.Ema(0, data.Length - 1, data, 50, out beginIndex, out outNBElements, emaValues);
            // if (statusEma == Core.RetCode.Success && outNBElements > 0)
            // {
            //     for (int i = 0; i < quotationList.Count -49; i++)
            //     {
            //         quotationList[i + 49].Ema = emaValues[i];
            //     }
            // }

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
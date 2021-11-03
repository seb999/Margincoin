using System;
using System.Collections.Generic;
using System.Linq;
using MarginCoin.Class;
using TicTacTec.TA.Library;

namespace MarginCoin.Misc
{
    public static class TradeIndicator
    {

        public static void CalculateIndicator(ref List<Quotation> quotationList)
        {
            var data = quotationList.Select(p => p.c).ToArray();
            int beginIndex;
            int outNBElements;
            double[] rsiValues = new double[data.Length];
            double[] outMACD = new double[data.Length];
            double[] outMACDSignal = new double[data.Length];
            double[] outMACDHist = new double[data.Length];
            double[] emaValues = new double[data.Length];

            //Calculate RSI
            var statusRsi = Core.Rsi(0, data.Length - 1, data, 50, out beginIndex, out outNBElements, rsiValues);
            if (statusRsi == Core.RetCode.Success && outNBElements > 0)
            {
                for (int i = 0; i <= outNBElements - 1; i++)
                {
                    quotationList[i + beginIndex].Rsi = (float)Math.Round(rsiValues[i], 2);
                }
            }

            //Calculate MACD
            var statusMacd = Core.MacdFix(0, data.Length - 1, data, 2, out beginIndex, out outNBElements, outMACD, outMACDSignal, outMACDHist);
            if (statusMacd == Core.RetCode.Success && outNBElements > 0)
            {
                for (int i = 0; i < outNBElements; i++)
                {
                    quotationList[i + beginIndex].Macd = outMACD[i] > 1 ? (float)Math.Round(outMACD[i], 3) : (float)Math.Round(outMACD[i], 6);
                    quotationList[i + beginIndex].MacdHist = Math.Abs(outMACDHist[i]) > 1 ? (float)Math.Round(outMACDHist[i], 3) : (float)Math.Round(outMACDHist[i], 6);
                    quotationList[i + beginIndex].MacdSign = outMACDSignal[i] > 1 ? (float)Math.Round(outMACDSignal[i], 3) : (float)Math.Round(outMACDSignal[i], 6);
                }
            }

            //Calculate EMA50
            var statusEma = Core.Ema(0, data.Length - 1, data, 50, out beginIndex, out outNBElements, emaValues);
            if (statusEma == Core.RetCode.Success && outNBElements > 0)
            {
                for (int i = 0; i < outNBElements; i++)
                {
                    quotationList[i + beginIndex].Ema = (float)Math.Round(emaValues[i], 2);
                }
            }

            foreach (var (quote, index) in quotationList.Select((v, i)=>(v, i))) {
                  if(index == 0)continue;
                  var PP = ((quotationList[index-1].h + quotationList[index-1].l + quotationList[index-1].c) / 3);
                    quote.PivotPoint = new PivotPOint()
                    {
                        R1 = 2 * PP - quotationList[index-1].l,
                        S1 = 2 * PP - quotationList[index-1].h,
                        R2 = PP + quotationList[index-1].h - quotationList[index-1].l,
                        S2 = (PP - quotationList[index-1].h + quotationList[index-1].l),
                        R3 = (quotationList[index-1].h + 2 * (PP - quotationList[index-1].l)),
                        S3 = (quotationList[index-1].l - 2 * (quotationList[index-1].h - PP)),
                    };
            }
        }
    }
}
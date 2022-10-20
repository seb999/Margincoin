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
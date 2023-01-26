using MarginCoin.Class;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MarginCoin.Misc
{
    public static class Helper
    {
        public static T deserializeHelper<T>(string jsonStream)
        {
            try
            {
                JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions
                {
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                };

                return JsonSerializer.Deserialize<T>(jsonStream, jsonSerializerOptions);
            }
            catch (System.Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
        }

        public static double ToDouble(string myString)
        {
            myString = myString.Replace(",", ".");
            return double.Parse(myString, CultureInfo.InvariantCulture);
        }

        //private double CalculateAvragePrice(BinanceOrder myOrder)
        //{
        //    var executedAmount = myOrder.fills.Sum(p => Helper.ToDouble(p.price) * Helper.ToDouble(p.qty));
        //    var executedQty = myOrder.fills.Sum(p => Helper.ToDouble(p.qty));
        //    return executedAmount / executedQty;
        //}

        //review by AI
        public static double CalculateAvragePrice(BinanceOrder myOrder)
        {
            double executedAmount = myOrder.fills
                .Sum(fill => Helper.ToDouble(fill.price) * Helper.ToDouble(fill.qty));
            double executedQty = myOrder.fills
                .Sum(fill => Helper.ToDouble(fill.qty));
            return executedAmount / executedQty;
        }
    }
}
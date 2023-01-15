using System;
using System.Collections.Generic;
using System.Globalization;
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
    }
}
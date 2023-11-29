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

        public static int GetNumberDecimal(string value)
        {
            // Find the position of the decimal point
            value = value.Replace(",", ".");
            int decimalPointIndex = value.IndexOf('.');
            int decimalDigitIndex = value.IndexOf('1');

            // If there is no decimal point, or if it's the last character, there are no decimal places
            if (decimalPointIndex == -1 || decimalDigitIndex == 0)
            {
                return 0;
            }

            // Calculate the number of decimal places
            int decimalPlaces = decimalDigitIndex - decimalPointIndex;
            return decimalPlaces;
        }
    }
}
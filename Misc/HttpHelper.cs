using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MarginCoin.Misc
{
    public static class HttpHelper
    {
        public static T GetApiData<T>(Uri apiUri)
        {
            return GetApiData<T>(apiUri, "");
        }

        public static T GetApiData<T>(Uri apiUri, string apiKey)
        {
            JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
            };

            using (var client = new HttpClient())
            {
                client.BaseAddress = apiUri;
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                // client.DefaultRequestHeaders.Add("X-MBX-APIKEY", apiKey);

                var response = client.GetAsync("").Result;
                if (response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<T>(response.Content.ReadAsStringAsync().Result, jsonSerializerOptions);
                }
                else
                {
                    return default(T);
                }
            }
        }

        public static T PostApiData<T>( Uri apiUri, string apiKey, HttpContent data)
        {
            JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("X-MBX-APIKEY", apiKey);
                
                var response = client.PostAsync(apiUri, data).Result;
               
                if (response.IsSuccessStatusCode)
                {
                    var toto = response.Content.ReadAsStringAsync().Result;
                    Console.WriteLine(toto);
                    return JsonSerializer.Deserialize<T>(response.Content.ReadAsStringAsync().Result, jsonSerializerOptions);
                }
                else
                {
                    Console.WriteLine(response.IsSuccessStatusCode);
                    return default(T);
                }
            }
        }
    }
}
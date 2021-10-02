using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MarginCoin.Misc
{
    public static class HttpHelper
    {
        public static T GetApiData<T>(Uri ApiUri)
        {
            JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions
            {
                //PropertyNameCaseInsensitive = true,   it is faster if it is sensitive the case
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
            };

            using (var client = new HttpClient())
            {
                client.BaseAddress = ApiUri;
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
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
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TrandingBoardController : ControllerBase
    {
        private static string API_KEY = "de2af521-9c3b-4ba9-9f55-0ff85ec9ad8f";

        [HttpGet("[action]")]
        public string GetCoinList(){
            var URL = new UriBuilder("https://pro-api.coinmarketcap.com/v1/cryptocurrency/listings/latest");

            var queryString = HttpUtility.ParseQueryString(string.Empty);
            queryString["start"] = "1";
            queryString["limit"] = "5000";
            queryString["convert"] = "USD";

            URL.Query = queryString.ToString();

            var client = new WebClient();
            client.Headers.Add("X-CMC_PRO_API_KEY", API_KEY);
            client.Headers.Add("Accepts", "application/json");

            var toto = client.DownloadString(URL.ToString());
            return "";
        }

        // public List<SymbolTransfer> GetSymbolList(BaseMarket baseMarket)
        // {
        //     Uri apiUrl = new Uri("https://api.binance.com/api/v1/ticker/24hr");

        //     //Get data from Binance API
        //     List<SymbolTransfer> coinList = HttpHelper.GetApiData<List<SymbolTransfer>>(apiUrl);


        //     return coinList;
        // }
    }
}

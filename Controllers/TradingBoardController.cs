using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MarginCoin.ClassTransfer;
using MarginCoin.Misc;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TradingBoardController : ControllerBase
    {
        /// <summary>
        /// Return list of coin with last price for default page
        /// </summary>
        /// <returns></returns>
        [HttpGet("[action]")]
        public List<SymbolTransfer> GetSymbolList()
        {
            Uri apiUrl = new Uri("https://api.binance.com/api/v3/ticker/24hr");
            //Get data from Binance API
            List<SymbolTransfer> coinList = HttpHelper.GetApiData<List<SymbolTransfer>>(apiUrl);
            // Filter result
            coinList = coinList.Where(p => p.symbol.EndsWith("USD")).Select(p => p).ToList();
            //Remove obsolete coins
            coinList = coinList.Where(p => p.volume != 0 && p.openPrice != 0).Select(p => p).ToList();
            // Shorten Symbol
            Helper.ShortenSymbol(ref coinList);

            return coinList;
        }

        [HttpGet("[action]/{symbol}")]
        public double GetSymbol24hr(string symbol, string timestamp)
        {
            string apiUrl = string.Format("https://api.binance.com/api/v3/ticker/24hr?symbol={0}", symbol);
            SymbolTransfer symbolTransfer = HttpHelper.GetApiData<SymbolTransfer>(new Uri(apiUrl));
            
            return symbolTransfer.priceChangePercent;
        }
    }
}

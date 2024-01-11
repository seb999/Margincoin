using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MarginCoin.Model;
using System.Globalization;
using MarginCoin.Class;
using MarginCoin.Misc;
using System.Web;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ActionController : ControllerBase
    {
        private readonly ApplicationDbContext _appDbContext;

        private static string COINMARKETCAP_API_KEY = "de2af521-9c3b-4ba9-9f55-0ff85ec9ad8f";

        public ActionController([FromServices] ApplicationDbContext appDbContext)
        {
            _appDbContext = appDbContext;
        }

        [HttpGet("[action]/{symbol}")]
        public MacdSlope MacdSlope(string symbol)
        {
            List<Candle> candleSymbol = Global.candleMatrix.ToList().Where(p => p.Last().s == symbol).Select(p => p).FirstOrDefault();
            if (candleSymbol == null) return null;
            return TradeHelper.CalculateMacdSlope(candleSymbol.ToList(), Global.interval);
        }

        [HttpGet("[action]")]
        public void SyncBinanceSymbol()
        {
            Global.syncBinanceSymbol = true;
        }

        [HttpGet("[action]")]
        public List<Symbol> GetSymbolList()
        {
            if (Global.isProd)
            {
                return _appDbContext.Symbol.Where(p => p.IsOnProd != 0 && p.Rank < Global.nbrOfSymbol).OrderBy(p => p.Rank).ToList();
            }
            else
            {
                return _appDbContext.Symbol.Where(p => p.IsOnTest != 0 && p.Rank < Global.nbrOfSymbol).OrderBy(p => p.Rank).ToList();
            }
        }

        /// <summary>
        /// Retrun list to top 60 coins from DB
        /// </summary>
        /// <returns></returns>
        public List<Symbol> GetSymbolBaseList()
        {
            if (Global.isProd)
            {
                return _appDbContext.Symbol.Where(p => p.IsOnProd != 0 && p.Rank < 100).OrderBy(p => p.Rank).ToList();
            }
            else
            {
                return _appDbContext.Symbol.Where(p => p.IsOnTest != 0 && p.Rank < 100).OrderBy(p => p.Rank).ToList();
            }
        }



        //Used once to copy all symbol from binance to my Symbol table
        public void UpdateDbSymbol(List<string> symbolList)
        {
            foreach (var symbolToAdd in symbolList)
            {
                if (_appDbContext.Symbol.Where(p => p.SymbolName == symbolToAdd).Select(p => p).FirstOrDefault() == null)
                {
                    _appDbContext.Symbol.Add(new Symbol() { IsOnProd = 1, SymbolName = symbolToAdd });
                }
            }

            _appDbContext.SaveChanges();
        }

        internal void UpdateDbSymbolCap(List<Content> coinMarketCapList)
        {
            var toto = coinMarketCapList.OrderBy(p => p.cmc_rank);
            foreach (var coin in coinMarketCapList.OrderBy(p => p.cmc_rank))
            {
                var mySymbol = _appDbContext.Symbol.Where(p => p.SymbolName == $"{coin.symbol}USDT").FirstOrDefault();
                if (mySymbol != null)
                {
                    // mySymbol.Capitalisation = coin.quote.USD.market_cap;
                    mySymbol.Rank = coin.cmc_rank;
                    mySymbol.Capitalisation = mySymbol.Capitalisation == null ? -1 : mySymbol.Capitalisation;
                    _appDbContext.Symbol.Update(mySymbol);
                    _appDbContext.SaveChanges();
                }
            }
        }

        public void UpdateCoinMarketCap()
        {
            //var URL = new UriBuilder("https://sandbox-api.coinmarketcap.com/v1/cryptocurrency/listings/latest");
            var URL = new UriBuilder("https://pro-api.coinmarketcap.com/v1/cryptocurrency/listings/latest");

            var queryString = HttpUtility.ParseQueryString(string.Empty);
            queryString["start"] = "1";
            queryString["limit"] = "1000";
            queryString["convert"] = "USD";
            URL.Query = queryString.ToString();

            Coinmarketcap coinQuotation = HttpHelper.GetApiDataCMC<Coinmarketcap>(URL.Uri, COINMARKETCAP_API_KEY);

            UpdateDbSymbolCap(coinQuotation.data);
        }

    }
}
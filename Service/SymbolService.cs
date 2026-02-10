using System.Collections.Generic;
using System.Linq;
using MarginCoin.Class;
using MarginCoin.Configuration;
using MarginCoin.Model;
using MarginCoin.Misc;
using Microsoft.Extensions.Options;
using System.Web;
using System;

namespace MarginCoin.Service
{
    public interface ISymbolService
    {
        List<Symbol> GetTopSymbols(int count);
        void SyncBinanceSymbols(List<string> symbolList);
        void UpdateCoinMarketCap();
    }

    public class SymbolService : ISymbolService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly CoinMarketCapConfiguration _cmcConfig;

        public SymbolService(
            ApplicationDbContext dbContext,
            IOptions<CoinMarketCapConfiguration> cmcConfig)
        {
            _dbContext = dbContext;
            _cmcConfig = cmcConfig.Value;
        }

        /// <summary>
        /// Get top N symbols by market cap rank
        /// </summary>
        public List<Symbol> GetTopSymbols(int count)
        {
            return _dbContext.Symbol
                .Where(p => p.Rank.HasValue && p.Rank <= count)
                .OrderBy(p => p.Rank)
                .ToList();
        }

        /// <summary>
        /// Sync symbols from Binance to database
        /// </summary>
        public void SyncBinanceSymbols(List<string> symbolList)
        {
            foreach (var symbolName in symbolList)
            {
                var existing = _dbContext.Symbol.FirstOrDefault(p => p.SymbolName == symbolName);
                if (existing == null)
                {
                    _dbContext.Symbol.Add(new Symbol { SymbolName = symbolName });
                }
            }
            _dbContext.SaveChanges();
        }

        /// <summary>
        /// Update market cap rankings from CoinMarketCap API
        /// </summary>
        public void UpdateCoinMarketCap()
        {
            var URL = new UriBuilder("https://pro-api.coinmarketcap.com/v1/cryptocurrency/listings/latest");
            var queryString = HttpUtility.ParseQueryString(string.Empty);
            queryString["start"] = "1";
            queryString["limit"] = "1000";
            queryString["convert"] = "USD";
            URL.Query = queryString.ToString();

            var coinQuotation = HttpHelper.GetApiDataCMC<Coinmarketcap>(URL.Uri, _cmcConfig.ApiKey);

            if (coinQuotation?.data == null) return;

            foreach (var coin in coinQuotation.data.OrderBy(p => p.cmc_rank))
            {
                var symbol = _dbContext.Symbol.FirstOrDefault(p => p.SymbolName == $"{coin.symbol}USDC");
                if (symbol != null)
                {
                    symbol.Rank = coin.cmc_rank;
                    symbol.Capitalisation = symbol.Capitalisation ?? -1;
                    _dbContext.Symbol.Update(symbol);
                    _dbContext.SaveChanges();
                }
            }
        }
    }
}

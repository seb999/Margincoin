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
        List<Symbol> GetTradingSymbols();
        List<Symbol> GetBaseSymbols();
        void SyncBinanceSymbols(List<string> symbolList);
        void UpdateCoinMarketCap();
    }

    public class SymbolService : ISymbolService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly TradingConfiguration _tradingConfig;
        private readonly CoinMarketCapConfiguration _cmcConfig;
        private readonly ITradingState _tradingState;

        public SymbolService(
            ApplicationDbContext dbContext,
            IOptions<TradingConfiguration> tradingConfig,
            IOptions<CoinMarketCapConfiguration> cmcConfig,
            ITradingState tradingState)
        {
            _dbContext = dbContext;
            _tradingConfig = tradingConfig.Value;
            _cmcConfig = cmcConfig.Value;
            _tradingState = tradingState;
        }

        /// <summary>
        /// Get list of symbols to trade based on current environment (prod/test)
        /// </summary>
        public List<Symbol> GetTradingSymbols()
        {
            return GetSymbols(_tradingConfig.NumberOfSymbols);
        }

        /// <summary>
        /// Get base list of top 100 symbols for monitoring
        /// </summary>
        public List<Symbol> GetBaseSymbols()
        {
            return GetSymbols(100);
        }

        private List<Symbol> GetSymbols(int maxRank)
        {
            var query = _dbContext.Symbol.AsQueryable();

            if (_tradingState.IsProd)
            {
                query = query.Where(p => p.IsOnProd != 0 && p.Capitalisation > 0 && p.Rank<= maxRank);
            }
            else
            {
                query = query.Where(p => p.IsOnTest != 0 && p.Rank <= maxRank);
            }

            return query.OrderBy(p => p.Rank).ToList();
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
                    _dbContext.Symbol.Add(new Symbol { IsOnProd = 1, SymbolName = symbolName });
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

using System;
using System.Linq;
using System.Text.Json;
using MarginCoin.Configuration;
using MarginCoin.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GlobalsController : ControllerBase
    {
        private readonly ITradingState _tradingState;
        private readonly ISymbolService _symbolService;
        private readonly TradingConfiguration _tradingConfig;

        public GlobalsController(
            ITradingState tradingState,
            ISymbolService symbolService,
            IOptions<TradingConfiguration> tradingConfig)
        {
            _tradingState = tradingState;
            _symbolService = symbolService;
            _tradingConfig = tradingConfig.Value;
        }

        [HttpGet("[action]")]
        public bool GetServer() => _tradingState.IsProd;

        [HttpGet("[action]")]
        public bool GetOrderType() => _tradingState.IsMarketOrder;

        [HttpGet("[action]/{isMarketOrder}")]
        public void SetOrderType(bool isMarketOrder)
        {
            _tradingState.IsMarketOrder = isMarketOrder;
        }

        [HttpGet("[action]/{isProd}")]
        public void SetServer(bool isProd)
        {
            _tradingState.IsProd = isProd;
            _tradingState.SymbolWeTrade = _symbolService.GetTopSymbols(_tradingConfig.NumberOfSymbols);
        }

        [HttpGet("[action]/{isOpen}")]
        public void SetTradeParameter(bool isOpen)
        {
            _tradingState.IsTradingOpen = isOpen;
        }

        [HttpGet("[action]")]
        public string GetInterval() => JsonSerializer.Serialize(_tradingConfig.Interval);

        [HttpGet("[action]")]
        public object GetMemoryDiagnostics()
        {
            var candleMatrixSymbolCount = _tradingState.CandleMatrix.Count;
            var candleMatrixTotalCandles = _tradingState.CandleMatrix.Sum(list => list.Count);
            var candleMatrixAvgCandlesPerSymbol = candleMatrixSymbolCount > 0
                ? candleMatrixTotalCandles / candleMatrixSymbolCount
                : 0;

            var processMemory = GC.GetTotalMemory(false) / 1024.0 / 1024.0; // MB
            var gen0Collections = GC.CollectionCount(0);
            var gen1Collections = GC.CollectionCount(1);
            var gen2Collections = GC.CollectionCount(2);

            return new
            {
                timestamp = DateTime.UtcNow,
                collections = new
                {
                    candleMatrix = new
                    {
                        symbolCount = candleMatrixSymbolCount,
                        totalCandles = candleMatrixTotalCandles,
                        avgCandlesPerSymbol = candleMatrixAvgCandlesPerSymbol
                    },
                    allMarketData = new
                    {
                        count = _tradingState.AllMarketData.Count
                    },
                    marketStreamOnSpot = new
                    {
                        count = _tradingState.MarketStreamOnSpot.Count
                    },
                    onHold = new
                    {
                        count = _tradingState.OnHold.Count,
                        symbols = _tradingState.OnHold.Keys.ToList()
                    },
                    symbolWeTrade = new
                    {
                        count = _tradingState.SymbolWeTrade.Count
                    },
                    symbolBaseList = new
                    {
                        count = _tradingState.SymbolBaseList.Count
                    }
                },
                memory = new
                {
                    managedMemoryMB = Math.Round(processMemory, 2),
                    gcCollections = new
                    {
                        gen0 = gen0Collections,
                        gen1 = gen1Collections,
                        gen2 = gen2Collections
                    }
                }
            };
        }
    }
}

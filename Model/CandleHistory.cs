using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarginCoin.Model
{
    /// <summary>
    /// Stores historical candle data for all trading symbols.
    /// Used for centralized market data collection instead of per-symbol WebSockets.
    /// </summary>
    public class CandleHistory
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Trading symbol (e.g., "BTCUSDC", "ETHUSDT")
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string Symbol { get; set; }

        /// <summary>
        /// Candle interval (e.g., "5m", "30m", "1h")
        /// </summary>
        [Required]
        [MaxLength(5)]
        public string Interval { get; set; }

        /// <summary>
        /// Candle open time (Unix timestamp in milliseconds)
        /// </summary>
        public long OpenTime { get; set; }

        /// <summary>
        /// Candle close time (Unix timestamp in milliseconds)
        /// </summary>
        public long CloseTime { get; set; }

        /// <summary>
        /// Open price
        /// </summary>
        public double Open { get; set; }

        /// <summary>
        /// High price
        /// </summary>
        public double High { get; set; }

        /// <summary>
        /// Low price
        /// </summary>
        public double Low { get; set; }

        /// <summary>
        /// Close price
        /// </summary>
        public double Close { get; set; }

        /// <summary>
        /// Trading volume
        /// </summary>
        public double Volume { get; set; }

        /// <summary>
        /// Is this candle closed/finalized?
        /// </summary>
        public bool IsClosed { get; set; }

        /// <summary>
        /// Price change percent (calculated)
        /// </summary>
        public double PriceChangePercent { get; set; }

        // Technical Indicators (calculated after enough candles are available)
        public double? RSI { get; set; }
        public double? MACD { get; set; }
        public double? MACDSignal { get; set; }
        public double? MACDHist { get; set; }
        public double? MACDSlope { get; set; }
        public double? EMA { get; set; }
        public double? StochSlowK { get; set; }
        public double? StochSlowD { get; set; }
        public double? ATR { get; set; }

        /// <summary>
        /// Timestamp when this record was created/updated
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        // Composite index for fast queries: Symbol + Interval + OpenTime
        [NotMapped]
        public string CompositeKey => $"{Symbol}_{Interval}_{OpenTime}";
    }
}

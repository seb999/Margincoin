namespace MarginCoin.Configuration
{
    /// <summary>
    /// Single source of truth for all trading configuration settings.
    /// Configured via appsettings.json
    /// </summary>
    public class TradingConfiguration
    {
        // Basic Trading Settings
        public string Interval { get; set; } = "30m";
        public string MaxCandle { get; set; } = "50";
        public int NumberOfSymbols { get; set; } = 18;
        public int MaxOpenTrades { get; set; } = 3;
        public double QuoteOrderQty { get; set; } = 1500;
        public double StopLossPercentage { get; set; } = 2;
        public double TakeProfitPercentage { get; set; } = 0.5;
        public double OrderOffset { get; set; } = 0.05;
        public string SpotTickerTime { get; set; } = "!ticker_4h@arr";
        public int PrevCandleCount { get; set; } = 2;

        // Entry Logic Configuration
        /// <summary>
        /// Minimum number of symbols that must be up to allow trading
        /// Exception: allows trading if market spread is extreme (below MaxSpreadOverride)
        /// </summary>
        public int MinConsecutiveUpSymbols { get; set; } = 30;

        /// <summary>
        /// Market spread threshold that overrides MinConsecutiveUpSymbols
        /// If a symbol is down more than this %, allows trading regardless of market breadth
        /// WARNING: Negative value, e.g., -5 means -5% (crash scenario)
        /// </summary>
        public double MaxSpreadOverride { get; set; } = -5.0;

        /// <summary>
        /// Minimum ML/AI prediction score required for entry (0.0 - 1.0)
        /// Lower value = more trades, higher value = more selective
        /// </summary>
        public double MinAIScore { get; set; } = 0.60;

        /// <summary>
        /// Minimum percentage price increase required to validate strong upward movement
        /// Used to filter out weak micro-movements
        /// </summary>
        public double MinPercentageUp { get; set; } = 0.2;

        /// <summary>
        /// Minimum RSI value for entry (avoid oversold conditions)
        /// </summary>
        public double MinRSI { get; set; } = 40;

        /// <summary>
        /// Maximum RSI value for entry (avoid overbought conditions)
        /// </summary>
        public double MaxRSI { get; set; } = 80;

        // Trend Score Configuration
        /// <summary>
        /// Minimum trend score required to enter a long position
        /// Trend score ranges from -5 to +5 based on multiple indicators
        /// Recommended: 3 (strong uptrend confirmation)
        /// </summary>
        public int MinTrendScoreForEntry { get; set; } = 3;

        /// <summary>
        /// Trend score threshold below which to exit positions early
        /// Recommended: -3 (strong downtrend detected)
        /// </summary>
        public int TrendScoreExitThreshold { get; set; } = -3;

        /// <summary>
        /// Use weighted trend score (gives more importance to EMA and MACD)
        /// If false, all indicators weighted equally
        /// </summary>
        public bool UseWeightedTrendScore { get; set; } = false;

        /// <summary>
        /// AI veto threshold - if AI predicts "down" with confidence above this, block entry
        /// Recommended: 0.70 (70% confidence in downward movement)
        /// </summary>
        public double AIVetoConfidence { get; set; } = 0.70;

        // Exit Logic Configuration
        /// <summary>
        /// Minutes to wait before killing a trade that hasn't gone above entry price
        /// Used to cut losses on stalled positions
        /// </summary>
        public int TimeBasedKillMinutes { get; set; } = 15;

        /// <summary>
        /// If true, tighten stop loss when trend score weakens (becomes 0 or negative)
        /// </summary>
        public bool EnableDynamicStopLoss { get; set; } = true;

        /// <summary>
        /// Percentage to tighten stop loss when trend weakens (e.g., 0.5 = 0.5% from current price)
        /// Only applies if EnableDynamicStopLoss is true
        /// </summary>
        public double WeakTrendStopLossPercentage { get; set; } = 0.5;
    }
}

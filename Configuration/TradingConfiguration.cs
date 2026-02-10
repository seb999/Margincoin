namespace MarginCoin.Configuration
{
    /// <summary>
    /// Static trading configuration from appsettings.json.
    /// These settings require application restart to change and should be modified carefully.
    /// For runtime-adjustable settings, see RuntimeTradingSettings (stored in database).
    /// </summary>
    public class TradingConfiguration
    {
        // Basic Trading Settings (Static)
        public string Interval { get; set; } = "30m";
        public string MaxCandle { get; set; } = "50";
        public int NumberOfSymbols { get; set; } = 5;
        public double OrderOffset { get; set; } = 0.05;
        public string SpotTickerTime { get; set; } = "!miniTicker@arr";
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

        // Advanced Configuration (Static - requires restart)
        /// <summary>
        /// Enable/disable ML prediction calls entirely.
        /// </summary>
        public bool EnableMLPredictions { get; set; } = false;

        // OpenAI Configuration
        /// <summary>
        /// Enable/disable OpenAI-powered trading signals
        /// </summary>
        public bool EnableOpenAISignals { get; set; } = true;

        /// <summary>
        /// Minimum OpenAI confidence level required for entry (0.0 - 1.0)
        /// Higher value = more selective trades based on AI confidence
        /// </summary>
        public double MinOpenAIConfidence { get; set; } = 0.7;

        /// <summary>
        /// Minimum OpenAI trading score required for entry (-10 to +10)
        /// Positive scores indicate buy signals, negative indicate sell
        /// Recommended: 6 for strong buy signals
        /// </summary>
        public int MinOpenAIScore { get; set; } = 6;
    }
}

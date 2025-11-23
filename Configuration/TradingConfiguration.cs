namespace MarginCoin.Configuration
{
    /// <summary>
    /// Single source of truth for all trading configuration settings.
    /// Configured via appsettings.json
    /// </summary>
    public class TradingConfiguration
    {
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
    }
}

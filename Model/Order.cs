using System.ComponentModel.DataAnnotations;

namespace MarginCoin.Model
{
    public class Order
    {
        [Key]
        public int Id { get; set; }
        public string Symbol { get; set; }
        public double Quantity { get; set; }
        public double OpenPrice { get; set; }
        public double HighPrice { get; set; }
        public double LowPrice { get; set; }
        public double ClosePrice { get; set; }
        public double Volume { get; set; }
        public double StopLose { get; set; }
        public double TakeProfit { get; set; }
        public double Profit { get; set; }
        public double Fee { get; set; }
        public int IsClosed { get; set; }
        public string Type { get; set; }
        public string Cat { get; set; }
        public string OpenDate { get; set; }
        public string CloseDate { get; set; }
        public double RSI { get; set; }
        public double RSI_1 { get; set; }
        public double RSI_2 { get; set; }
        public double MACDHist { get; set; }
        public double MACDHist_1 { get; set; }
        public double MACDHist_2 { get; set; }
        public double MACDHist_3 { get; set; }
        public int PredictionLBFGS { get; set; }
        public int PredictionLDSVM { get; set; }
        public int PredictionSDA { get; set; }
        public double MACD { get; set; }
        public double MACDSign { get; set; }
        public double EMA { get; set; }
        public double StochSlowD { get; set; }
        public double StochSlowK { get; set; }
        public double R1 { get; set; }
        public double S1 { get; set; }
        public int Lock { get; set; }
        public string MarketTrend { get; set; }
        public double OrderId { get; set; }
        public string Status { get; set; }
        public double MLBuyScore { get; set; }
        public double MLSellScore { get; set; }
    }
}
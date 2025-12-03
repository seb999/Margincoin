using System;

namespace MarginCoin.Class
{
    public class AIPredictionDto
    {
        public string Symbol { get; set; }
        public string Pair { get; set; }
        public string Prediction { get; set; }
        public double Confidence { get; set; }
        public double ExpectedReturn { get; set; }
        public float? UpProbability { get; set; }
        public float? DownProbability { get; set; }
        public float? SidewaysProbability { get; set; }
        public int? TrendScore { get; set; }
        public DateTime? Timestamp { get; set; }
    }
}

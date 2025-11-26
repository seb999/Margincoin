using System;

namespace MarginCoin.MLClass
{
    public class MLPrediction
    {
        public string Symbol { get; set; }
        public string PredictedLabel { get; set; }
        public float[] Score { get; set; }

        // Additional fields for LSTM/Transformer model
        public double Confidence { get; set; }
        public double ExpectedReturn { get; set; }
        public int? TrendScore { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
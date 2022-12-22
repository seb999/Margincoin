using System;

namespace MarginCoin.MLClass
{
    public class MLPrediction
    {
        public string Symbol { get; set; }
        public string PredictedLabel { get; set; }
        public float[] Score { get; set; }  
    }
}
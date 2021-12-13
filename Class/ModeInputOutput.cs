using Microsoft.ML.Data;

namespace MarginCoin.Class
{
    public class Prediction
    {
        public class ModelInput
        {
            [LoadColumn(5)]
            public float v { get; set; }

            [LoadColumn(12)]
            public float Rsi { get; set; }

            [LoadColumn(15)]
            public float MacdHist { get; set; }
            
            [LoadColumn(17)]
            public float FuturePrice { get; set; }
        }

        public class ModelOutput
        {
            [ColumnName("Score")]
            public float FuturePrice { get; set; }
        }
    }
}
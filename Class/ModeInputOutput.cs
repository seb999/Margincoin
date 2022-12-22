using Microsoft.ML.Data;

namespace MarginCoin.Class
{
    public class Prediction
    {
        public class ModelInput2
        {
           [LoadColumn(13)]
            public float Rsi { get; set; }

            [LoadColumn(16)]
            public float MacdHistN3 { get; set; }

            [LoadColumn(17)]
            public float MacdHistN2 { get; set; }

            [LoadColumn(18)]
            public float MacdHistN1 { get; set; }

            [LoadColumn(19)]
            public float MacdHistN0 { get; set; }

            [LoadColumn(20)]
            public bool FuturePrice { get; set; }
        }

        public class ModelOutput2
        {
             [ColumnName("PredictedLabel")]
            public bool Prediction { get; set; }
            public float Probability;
            public float Score;
            public string ModelName { get; set; }
        }
    }
}
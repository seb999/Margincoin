
namespace MarginCoin.Class
{
    public class Candle
    {
        public string id { get; set; }
        public double T { get; set; }  // Open time
        public string s { get; set; } // Symbol name
        public double o { get; set; } // open price
        public double h { get; set; } // high price
        public double l { get; set; } // low price
        public double c { get; set; } // close price
        public double v { get; set; } // volum
        public double t { get; set; } // Close time
        public bool x { get; set; } // Is this kline closed? 
        public bool IsOnHold { get; set; }
        public double Rsi { get; set; }
        public double Macd { get; set; }
        public double MacdSign { get; set; }
        public double MacdHist { get; set; }
        public double Ema { get; set; }
        public double StochSlowK { get; set; }
        public double StochSlowD { get; set; }
        public PivotPOint PivotPoint { get; set; }
    }

    public class PivotPOint
    {
        public double R1 { get; set; }
        public double S1 { get; set; }
        public double R2 { get; set; }
        public double S2 { get; set; }
        public double R3 { get; set; }
        public double S3 { get; set; }

    }
}

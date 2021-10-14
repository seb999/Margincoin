
namespace MarginCoin.Misc
{
    public class Quotation
    {
        public double E { get; set; }  // Event time
        public double o { get; set; } // open price
        public double h { get; set; } // high price
        public double l { get; set; } // low price
        public double c { get; set; } // close price
        public double v { get; set; } // volum
        public double Rsi { get; set; }
        public double Macd { get; set; }
        public double MacdSign { get; set; }
        public double MacdHist { get; set; }
         public double MacdSign2 { get; set; }
        public double MacdHist2 { get; set; }

    }
}
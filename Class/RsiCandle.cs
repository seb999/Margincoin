
namespace MarginCoin.Class
{
    public class RsiCandle : Candle
    {
        public double P { get; set; } 
        public double rsi_o { get; set; } // open price
        public double rsi_h { get; set; } // high price
        public double rsi_l { get; set; } // low price
        public double rsi_c { get; set; } // close price
    }
}
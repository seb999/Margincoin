
namespace MarginCoin.Class
{
    public class MarketStream
    {
        public string e { get; set; }  // Event type
        public double E { get; set; }  // Event time
        public string s { get; set; }   // Symbol
        public double p { get; set; }    // Price change
        public double P { get; set; }      // Price change percent
        public double w { get; set; }      // Weighted average price
        public double x { get; set; }      // First trade(F)-1 price (first trade before the 24hr rolling window)
        public double c { get; set; }      // Last price
        public double Q { get; set; }      // Last quantity
        public double b { get; set; }      // Best bid price
        public double B { get; set; }      // Best bid quantity
        public double a { get; set; }      // Best ask price
        public double A { get; set; }      // Best ask quantity
        public double o { get; set; }      // Open price
        public double h { get; set; }      // High price
        public double l { get; set; }      // Low price
        public double v { get; set; }       // Total traded base asset volume
        public double q { get; set; }       // Total traded quote asset volume
        public double O { get; set; }       // Statistics open time
        public double C { get; set; }      // Statistics close time
        public double F { get; set; }       // First trade ID
        public double L { get; set; }      // Last trade Id
        public double n { get; set; }      // Total number of trades

    }
}

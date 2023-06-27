
namespace MarginCoin.Class
{
    public class MacdSlope
    {
        public double Slope { get; set; } 
        public Point P1 { get; set; }
        public Point P2 { get; set; } // open price
    }

    public class Point
    {
        public double x{ get; set; } 
        public double y{ get; set; } 
    }
}
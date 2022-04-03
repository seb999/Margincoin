using System.Collections.Generic;

namespace MarginCoin.Class
{
    public class StreamData
    {
        public string e { get; set; }// Event type
        public double E { get; set; }// Event time
        public string s { get; set; }// Symbol
        public Candle k { get; set; }
    }
}
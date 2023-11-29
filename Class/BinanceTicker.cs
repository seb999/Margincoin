using System.Collections.Generic;

namespace MarginCoin.Class
{
    public class BinanceTicker
    {
        public List<symbol> symbols { get; set; }
    }

    public class symbol
    {
        public List<filter> filters{ get; set; }
    }

     public class filter
    {
         public string filterType { get; set; }
         public string minPrice { get; set; }
         public string maxPrice { get; set; }
         public string tickSize { get; set; }
         public string minQty { get; set; }
         public string maxQty { get; set; }
         public string stepSize { get; set; }
    }
}
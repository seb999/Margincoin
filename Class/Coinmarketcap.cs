using System;
using System.Collections.Generic;

namespace MarginCoin.Class
{
    public class Coinmarketcap
    {
        public List<Content> data { get; set; }
    }

    public class Content
    {
        public int id { get; set; }
        public string name { get; set; }
        public string symbol { get; set; }
         public int cmc_rank { get; set; }
        public object self_reported_circulating_supply { get; set; }
        public object self_reported_market_cap { get; set; }

        //public Quote quote { get; set; }
    }

    public class Quote
    {
        public USD BTC { get; set; }
        public USD USD { get; set; }
    }

    public class USD
    {
        public double price { get; set; }
        public long volume_24h { get; set; }
        public double volume_change_24h { get; set; }
        public double percent_change_1h { get; set; }
        public double percent_change_24h { get; set; }
        public double percent_change_7d { get; set; }
        public double market_cap { get; set; }
        public int market_cap_dominance { get; set; }
        public double fully_diluted_market_cap { get; set; }
        public DateTime last_updated { get; set; }
    }
}
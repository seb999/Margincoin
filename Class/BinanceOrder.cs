using System.Collections.Generic;

namespace MarginCoin.Class
{
    public class BinanceOrder
    {
        public double orderId { get; set; }
        public string clientOrderId { get; set; }
        public string symbol { get; set; }
        public double orderListId { get; set; } //Unless OCO, value will be -1
        public long transactTime { get; set; }
        public string price { get; set; }
        public string origQty { get; set; }
        public string executedQty { get; set; }
        public string cummulativeQuoteQty { get; set; }
        public string status { get; set; }
        public string timeInForce { get; set; }
        public string type { get; set; }
        public string side { get; set; }

        public List<fills> fills { get; set; }
    }

    public class fills
    {
        public double tradeId { get; set; }
        public string price { get; set; }
        public string qty { get; set; }
        public string commission { get; set; }
        public string commissionAsset { get; set; }
    }
}
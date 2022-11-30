using System.Collections.Generic;

namespace MarginCoin.Class
{
    public class BinanceAccount
    {
        public double makerCommission { get; set; }
        public double takerCommission { get; set; }
        public double buyerCommission { get; set; }
        public double sellerCommission { get; set; }
        public bool canTrade { get; set; }
        public bool canWithdraw { get; set; }
        public bool canDeposit { get; set; }
        public bool brokered { get; set; }
        public string accountType { get; set; }
        public List<balances> balances { get; set; }
    }

    public class balances
    {
        public string asset { get; set; }
        public string free { get; set; }
        public string locked { get; set; }
    }
}
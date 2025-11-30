namespace MarginCoin.Misc
{
    public static class MyEnum
    {
        public enum BinanceHttpError
        {
            AccessFaulty,
            TooManyRequest,
            CheckAllowedIP,
            SellOrderExpired,
            WebSocketStopped,
            BadRequest,
        }

        public enum BinanceApiCall
        {
            BuyMarket,
            SellMarket,
            BuyLimit,
            SellLimit,
            Asset,
            Account,
            OrderStatus,
            CancelOrder,
            CancelSymbolOrder,
            Ticker
        }

        public enum TimeInForce
        {
            GTC,   //Good Til Canceled. An order will be on the book unless the order is canceled.
            IOC,    //Immediate Or Cancele. An order will try to fill the order as much as it can before the order expires.
            FOK     //Fill or Kill. An order will expire if the full order cannot be filled upon execution.
        }

        public enum OrderStatus
        {
            NEW,
            PARTIALLY_FILLED,
            FILLED,
            CANCELED,
            REJECTED,
            EXPIRED,
            EXPIRED_IN_MATCH
        }

        public enum OrderSide
        {
            BUY,
            SELL
        }

        public enum TrendDirection
        {
            Up,
            Down,
            Sideways
        }
    }
}
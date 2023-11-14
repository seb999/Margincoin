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
        }

        public enum TimeInForce
        {
            GTC,   //Good Til Cancele An order will be on the book unless the order is canceled.
            IOC,    //Immediate Or Cancele An order will try to fill the order as much as it can before the order expires.
            FOK     //Fill or Kill An order will expire if the full order cannot be filled upon execution.
        }
    }
}
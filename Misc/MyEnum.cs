using System;

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

        public enum PredictionDirection
        {
            Up,
            Down,
            Sideway,
            Off
        }
    }

    public static class PredictionDirectionExtensions
    {
        public static string ToLabel(this MyEnum.PredictionDirection direction)
        {
            return direction switch
            {
                MyEnum.PredictionDirection.Sideway => "sideways",
                _ => direction.ToString().ToLowerInvariant()
            };
        }

        public static bool TryParse(string value, out MyEnum.PredictionDirection direction)
        {
            if (string.Equals(value, "sideways", StringComparison.OrdinalIgnoreCase))
            {
                direction = MyEnum.PredictionDirection.Sideway;
                return true;
            }

            return Enum.TryParse(value, true, out direction);
        }
    }
}

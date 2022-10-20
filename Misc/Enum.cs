namespace MarginCoin.Misc
{
    public class Enum
    {
        public enum OrderType
        {
            Market,
            Limit
        }

        public enum TimeInForce
        {
            GTC,   //Good Til Cancele An order will be on the book unless the order is canceled.
            IOC,    //Immediate Or Cancele An order will try to fill the order as much as it can before the order expires.
            FOK     //Fill or Kill An order will expire if the full order cannot be filled upon execution.
        }
    }
}
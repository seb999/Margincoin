namespace MarginCoin.Configuration
{
    public class BinanceConfiguration
    {
        public BinanceEnvironment Test { get; set; }
        public BinanceEnvironment Production { get; set; }
    }

    public class BinanceEnvironment
    {
        public string PublicKey { get; set; }
        public string SecretKey { get; set; }
    }

    public class CoinMarketCapConfiguration
    {
        public string ApiKey { get; set; }
    }
}

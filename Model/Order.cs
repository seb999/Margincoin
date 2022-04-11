using System.ComponentModel.DataAnnotations;

namespace MarginCoin.Model
{
    public class Order
    {
        [Key]
        public int Id { get; set; }
        public string Symbol { get; set; }
        public double Quantity { get; set; }
        public double OpenPrice { get; set; }
        public double HighPrice { get; set; }
        public double LowPrice { get; set; }
        public double ClosePrice { get; set; }
        public double StopLose { get; set; }
        public double TakeProfit { get; set; }
        public double Profit { get; set; }
        public double Fee { get; set; }
        public int IsClosed { get; set; }
        public string Type { get; set; }
        public string OpenDate { get; set; }
        public string CloseDate { get; set; }
        public double RSI { get; set; }
        public double MACD{ get; set; }
    }
}
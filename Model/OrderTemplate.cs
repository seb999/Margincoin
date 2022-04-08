using System.ComponentModel.DataAnnotations;

namespace MarginCoin.Model
{
    public class OrderTemplate
    {
        [Key]
        public int Id { get; set; }
        public string Symbol { get; set; }
        public double Quantity { get; set; }
        public double Amount { get; set; }
        public double StopLose { get; set; }
        public double TakeProfit { get; set; }
        public int IsInactive { get; set; }
        public string DateAdded { get; set; }
        public string DateMod { get; set; }
    }
}
using System.ComponentModel.DataAnnotations;

namespace MarginCoin.Model
{
    public class OrderTemplate
    {
        [Key]
        public int Id { get; set; }
        public string Symbol { get; set; }
        public decimal Quantity { get; set; }
        public decimal Amount { get; set; }
        public int Levrage { get; set; }
        public int IsInactive { get; set; }
        public string DateAdded { get; set; }
        public string DateMod { get; set; }
    }
}
using System.ComponentModel.DataAnnotations;

namespace MarginCoin.Model
{
    public class Order
    {
        [Key]
        public int Id { get; set; }
        public string Symbol { get; set; }
        public decimal Quantity { get; set; }
        public decimal OpenPrice { get; set; }
        public decimal ClosePrice { get; set; }
        public int Margin { get; set; }
        public decimal StopLose { get; set; }
         public int IsClosed { get; set; }
         public string OpenDate { get; set; }
         public string CloseDate { get; set; }
    }
}
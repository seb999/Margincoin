using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MarginCoin.Model
{
    public class Order
    {
        [Key]
        public int OrderId { get; set; }
        public string OrderSymbol { get; set; }
        public decimal OrderAmount { get; set; }
        public decimal OrderOpenPrice { get; set; }
        public decimal OrderQuantity { get; set; }
        public decimal OrderStopLose { get; set; }
        public int OrderMargin { get; set; }
    }
}
using System.ComponentModel.DataAnnotations;

namespace MarginCoin.Model
{
    public class Symbol
    {
        [Key]
        public int Id { get; set; }
        public string SymbolName { get; set; }
        public double? Capitalisation { get; set; }
        public int? Rank { get; set; }
    }
}
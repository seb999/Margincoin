using System.ComponentModel.DataAnnotations;

namespace MarginCoin.Model
{
    public class Symbol
    {
        [Key]
        public int Id { get; set; }
        public string SymbolName { get; set; }
    }
}
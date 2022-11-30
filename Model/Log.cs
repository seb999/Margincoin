using System.ComponentModel.DataAnnotations;

namespace MarginCoin.Model
{
    public class Log
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string DateAdded { get; set; }
        
    }
}
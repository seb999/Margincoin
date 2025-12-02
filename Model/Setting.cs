using System.ComponentModel.DataAnnotations;

namespace MarginCoin.Model
{
    public class Setting
    {
        [Key]
        public string Key { get; set; }
        public string Value { get; set; }
    }
}

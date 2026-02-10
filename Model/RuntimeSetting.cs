using System.ComponentModel.DataAnnotations;

namespace MarginCoin.Model
{
    /// <summary>
    /// Database entity for runtime-adjustable trading settings
    /// </summary>
    public class RuntimeSetting
    {
        [Key]
        public string Key { get; set; }

        public string Value { get; set; }
    }
}

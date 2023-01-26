using System.Collections.Generic;
using System.Timers;
using MarginCoin.MLClass;

namespace MarginCoin.Service
{

    public interface IMLService
    {
        public List<MLPrediction> MLPredList { get; set; }
        public void InitML();
        public void StopML();
        public void UpdateML();
        public void CleanImageFolder();
    }
}
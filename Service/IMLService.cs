using System.Collections.Generic;
using System.Timers;
using MarginCoin.MLClass;
using static MarginCoin.Service.MLService;

namespace MarginCoin.Service
{
    public interface IMLService
    {
        public List<MLPrediction> MLPredList { get; set; }
        public void InitML(TimeElapseDelegate dddd);
        public void StopML();
        public void UpdateML();
        public void CleanImageFolder();
    }
}
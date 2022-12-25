using System.Collections.Generic;
using System.Timers;
using MarginCoin.MLClass;

namespace MarginCoin.Service
{
    
    public interface IMLService
    {     
        public List<MLPrediction> MLPredList { get; set; }

         public void ActivateML();
         public void GetUpdatedML();
         public void MLTimer_Elapsed(object sender, ElapsedEventArgs e);

         public void CleanMLImageFolder();
    }
}
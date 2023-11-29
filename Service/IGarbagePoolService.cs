using System.Timers;
using MarginCoin.Model;
using static MarginCoin.Service.GarbagePoolService;

namespace MarginCoin.Service
{
    public interface IGarbagePoolService
    {
        public void InitGarbagePool(TimeElapse timeElapse);
        public void StopML();
    }
}
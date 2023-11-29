using System.Linq;
using System.Timers;
using MarginCoin.Misc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MarginCoin.Model;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace MarginCoin.Service
{
    public class GarbagePoolService : IGarbagePoolService
    {
        public delegate void TimeElapse();
        private TimeElapse _timeElapseCallback;
        private ILogger _logger;
        private Timer garbageTimer = new Timer();

        public GarbagePoolService(ILogger<GarbagePoolService> logger)
        {
            _logger = logger;
            garbageTimer.Interval = 20000;
            garbageTimer.Elapsed += new ElapsedEventHandler(GarbageTimer_Elapsed);
        }

        public void InitGarbagePool(TimeElapse timeElapse)
        {
            _timeElapseCallback = timeElapse;
            garbageTimer.Start();
        }

        public void StopML()
        {
            garbageTimer.Stop();
        }

        private void GarbageTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
             _timeElapseCallback();
        }
    }
}
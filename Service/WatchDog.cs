using System.Timers;

namespace MarginCoin.Service
{
    public class WatchDog : IWatchDog
    {
        public delegate void MyMethod();
        private Timer _timer;
        private MyMethod _myMethod;
        public bool IsWebsocketSpotDown { get; set; }

        public WatchDog()
        {
            IsWebsocketSpotDown = false;
        }

        public void InitWatchDog(MyMethod myMethod)
        {
            _myMethod = myMethod;
            _timer = new Timer(60000); // check every minutes
            _timer.Elapsed += _timer_Elapsed;
            _timer.Start();
        }

       public void Clear()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Start();
            }
        }

        private void _timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            _myMethod();
        }
    }
}
﻿using static MarginCoin.Service.WatchDog;

namespace MarginCoin.Service
{
    public interface IWatchDog
    {
        bool IsWebsocketSpotDown { get; set; }

        void CallMethod(MyMethod myMethod);
        void Clear();
    }
}

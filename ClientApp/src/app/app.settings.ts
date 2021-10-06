// WHere to store those parameters ? 
// If we go live with multi-user account

import { Component } from '@angular/core';

export class AppSetting {
   finnhubKey = "bq68sg7rh5rc303ngeqg";
   intervalList = [
      { key: '15m', value: '15m' }, 
      { key: '1h', value: '1h' }, 
      { key: '4h', value: '4h' },
      { key: '6h', value: '6h' },
      { key: '8h', value: '8h' },
      { key: '12h', value: '12h' },
      { key: '1d', value: '1d' },
      { key: '1w', value: '1w' },
   ]
}

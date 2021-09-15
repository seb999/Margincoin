import { Component } from '@angular/core';

@Component({
  selector: 'app-wallet-component',
  templateUrl: './wallet.component.html'
})
export class WalletComponent {
  public currentCount = 0;

  public incrementCounter() {
    this.currentCount++;
  }
}

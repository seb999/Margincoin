import { Component } from '@angular/core';
import { HttpSettings, HttpService } from '../service/http.service';


@Component({
  selector: 'app-wallet',
  templateUrl: './wallet.component.html',
})
export class WalletComponent {
  public orderList = [] as any;

  constructor(
    private httpService: HttpService,
  ) {

  }

  async ngOnInit() {
    //Display list of open orders
    this.orderList = await this.getOpenOrder();
  }

  async getOpenOrder() {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Order/GetAllCompletedOrder/",
    };
    return await this.httpService.xhr(httpSetting);
  }
}

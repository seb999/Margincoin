import { Component } from '@angular/core';
import { NumericLiteral } from 'typescript';
import { Order } from '../class/order';
import { HttpSettings, HttpService } from '../service/http.service';


@Component({
  selector: 'app-wallet',
  templateUrl: './wallet.component.html',
})
export class WalletComponent {
  public orderList : Order [];
  public totalProfit : number;

  constructor(
    private httpService: HttpService,
  ) {

  }

  async ngOnInit() {
    //Display list of open orders
    this.orderList = await this.getOpenOrder();
    this.totalProfit = this.orderList.map(a => (a.profit-a.fee)).reduce(function(a, b)
    {
      return a + b;
    });
  }

  async getOpenOrder():Promise<Order[]> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Order/GetAllCompletedOrder/",
    };
    return await this.httpService.xhr(httpSetting);
  }
}

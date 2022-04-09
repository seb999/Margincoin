import { Component } from '@angular/core';
import { NumericLiteral } from 'typescript';
import { Order } from '../class/order';
import { HttpSettings, HttpService } from '../service/http.service';
import { SignalRService } from '../service/signalR.service';
import { ServerMsg } from '../class/serverMsg';


@Component({
  selector: 'app-wallet',
  templateUrl: './wallet.component.html',
})
export class WalletComponent {
  public orderList: Order[];
  public totalProfit: number;
  public serverMsg: ServerMsg;
  public showMessageInfo: boolean = false;

  constructor(
    private httpService: HttpService,
    private signalRService: SignalRService,
  ) {

  }

  async ngOnInit() {
    //Display list of open orders
    this.orderList = await this.getOpenOrder();

    this.calculateTotal();

    //Open listener on my API SignalR
    this.signalRService.startConnection();
    this.signalRService.addTransferChartDataListener();
    this.signalRService.onMessage().subscribe(async message => {
      this.serverMsg = message;
      this.showMessageInfo = true;
      if (this.serverMsg.msgName == 'refreshUI') {
        console.log("New order so you refresh the UI Please!!!!");
        this.orderList = await this.getOpenOrder();
        this.calculateTotal();
      }
      setTimeout(() => { this.showMessageInfo = false }, 700);
    });
  }

  calculateTotal(){
    if (this.orderList.length > 0) {
      this.totalProfit = this.orderList.map(a => (a.profit)).reduce(function (a, b) {
        if(a!=0) return a + b;
      });
    }
  }

  async getOpenOrder(): Promise<Order[]> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Order/GetAllCompletedOrder/",
    };
    return await this.httpService.xhr(httpSetting);
  }

  async monitorMarket(): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: 'https://localhost:5001/api/AutoTrade2/MonitorMarket',
    };
    return await this.httpService.xhr(httpSetting);
  }
}

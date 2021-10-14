import { Injectable } from "@angular/core";
import { Order } from "../class/order";
import { HttpService, HttpSettings } from "../service/http.service";

@Injectable({
  providedIn: 'root'
})
export class TradingboardHelper {

  constructor(private httpService: HttpService) {
  }

  async getIntradayData(symbol, interval): Promise<any> {
   console.log(interval);
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: 'https://api3.binance.com/api/v3/klines?symbol=' + symbol + '&interval=' + interval
    };
    return await this.httpService.xhr(httpSetting);
  }

  async getOpenOrder(symbol) {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Order/GetOpenOrder/" + symbol,
    };
    return await this.httpService.xhr(httpSetting);
  }

  async openOrder(model : any) {
    const httpSetting: HttpSettings = {
      method: "POST",
      url: location.origin + "/api/Order/OpenOrder/",
      data: {
        'symbol': model.symbol,
        'amount': model.amount,
        'quantity' : model.quantity,
        'openPrice': model.openPrice,
        'stopLose': model.stopLose,
        'margin' : model.margin,
      }
    };
    // this.showLabelSave = true;
    // setTimeout(() => {
    //   this.showLabelSave = false;
    // }, 4000)
    await this.httpService.xhr(httpSetting);
  }

  async closeOrder(orderId : number, price : number) {
    const httpSetting: HttpSettings = {
      method: "GET",
      url: location.origin + "/api/Order/CloseOrder/" + orderId + '/' + price,
    };
    await this.httpService.xhr(httpSetting);
  }

  async SaveOrderTemplate(data) {
    const httpSetting: HttpSettings = {
      method: 'POST',
      data: data,
      url: 'https://localhost:5001/api/AutoTrade/SaveOrderTemplate/',
    };
    return await this.httpService.xhr(httpSetting);
  }

  async testBeta() {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/AutoTrade/Activate",
    };
    return await this.httpService.xhr(httpSetting);
  }
}
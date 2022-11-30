import { Injectable } from "@angular/core";
import { Order } from "../class/order";
import { HttpService, HttpSettings } from "../service/http.service";

@Injectable({
  providedIn: 'root'
})
export class OrderDetailHelper {

  constructor(private httpService: HttpService) {
  }

  async getIntradayData(symbol, limit, interval): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: 'https://api3.binance.com/api/v3/klines?symbol=' + symbol + '&interval=' + interval + '&limit=' + 100
    };
    return await this.httpService.xhr(httpSetting);
  }

  async getOrder(id): Promise<Order> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Order/GetOrder/" + id,
    };
    return await this.httpService.xhr(httpSetting);
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
      url: 'https://localhost:5002/api/AutoTrade/SaveOrderTemplate/',
    };
    return await this.httpService.xhr(httpSetting);
  }
}
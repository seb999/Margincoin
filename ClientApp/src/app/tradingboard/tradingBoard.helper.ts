import { Injectable } from "@angular/core";
import { HttpService, HttpSettings } from "../service/http.service";

@Injectable({
  providedIn: 'root'
})
export class TradingboardHelper {

  constructor(private httpService: HttpService) {
  }

  async getIntradayData(symbol, interval): Promise<any> {
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
}
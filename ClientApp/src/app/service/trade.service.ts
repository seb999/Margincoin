import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { HttpSettings, HttpService } from '../service/http.service';
import { Order } from '../class/order';

@Injectable({
  providedIn: 'root'
})
export class TradeService {

  constructor(private httpService: HttpService) { }

  async setServer(isProd): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Globals/SetServer/" + isProd,
    };
    console.log(httpSetting);
    return await this.httpService.xhr(httpSetting);
  }

  async getActiveServer(): Promise<boolean> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Globals/GetServer",
    };
    console.log(httpSetting);
    return await this.httpService.xhr(httpSetting);
  }

  async setOrderType(isMarketOrder): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Globals/SetOrderType/" + isMarketOrder,
    };
    return await this.httpService.xhr(httpSetting);
  }

  async getOrderType(): Promise<boolean> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Globals/GetOrderType",
    };
    return await this.httpService.xhr(httpSetting);
  }

  async getInterval(): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Globals/GetInterval",
    };
    return await this.httpService.xhr(httpSetting);
  }

  async setTradeParam(isTradeOpen): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Globals/SetTradeParameter/" + isTradeOpen,
    };
    return await this.httpService.xhr(httpSetting);
  }

  async getAllOrder(fromDate): Promise<Order[]> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Order/GetAllOrderFromDate/" + fromDate,
    };
    return await this.httpService.xhr(httpSetting);
  }

  async getPendingOrder(): Promise<Order[]> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Order/GetPendingdOrder/",
    };
    return await this.httpService.xhr(httpSetting);
  }

  async closeTrade(orderId, lastPrice): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/AlgoTrade/CloseTrade/' + orderId + "/" + lastPrice,
    };
    return await this.httpService.xhr(httpSetting);
  }

  async sell(asset, qty): Promise<any> {
    let symbol = asset + "USDT";
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/Binance/Sell/' + symbol + "/" + qty
    };
    return await this.httpService.xhr(httpSetting);
  }

  async buy(asset, quoteQty): Promise<any> {
    let symbol = asset + "USDT";
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/Binance/Buy/' + symbol + "/" + quoteQty
    };
    return await this.httpService.xhr(httpSetting);
  }

  async getSymbolMacdSlope(symbol): Promise<any[]> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/Action/MacdSlope/' + symbol,
    };
    return await this.httpService.xhr(httpSetting);
  }

  async debugBuy(): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/AlgoTrade/TestBinanceBuy/',
    };
    return await this.httpService.xhr(httpSetting);
  }

  async syncBinanceSymbol(): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/AlgoTrade/SyncBinanceSymbol/',
    };
    return await this.httpService.xhr(httpSetting);
  }

  async getLog(): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/Log/GetLog',
    };
    return await this.httpService.xhr(httpSetting);
  }

  async mlUpdate(): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/AlgoTrade/UpdateML',
    };
    return await this.httpService.xhr(httpSetting);
  }

  async binanceAccount(): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/Binance/BinanceAccount',
    };
    return this.httpService.xhr(httpSetting);
  }

  async getSymbolList(): Promise<any[]> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/Action/GetSymbolList',
    };
    return await this.httpService.xhr(httpSetting);
  }

  async getSymbolPrice(): Promise<any[]> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/AlgoTrade/GetSymbolPrice',
    };
    return await this.httpService.xhr(httpSetting);
  }

  async cancelSymbolOrder(symbol): Promise<any[]> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/Binance/CancelSymbolOrder/' + symbol,
    };
    return await this.httpService.xhr(httpSetting);
  }
}
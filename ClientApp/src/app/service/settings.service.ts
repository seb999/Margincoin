import { Injectable } from '@angular/core';
import { HttpSettings, HttpService } from './http.service';

export interface TradingSettings {
  interval: string;
  maxOpenTrades: number;
  numberOfSymbols: number;
  quoteOrderQty: number;
  stopLossPercentage: number;
  takeProfitPercentage: number;
  orderOffset: number;
  minPercentageUp: number;
  minRSI: number;
  maxRSI: number;
  timeBasedKillMinutes: number;
}

@Injectable({ providedIn: 'root' })
export class SettingsService {
  constructor(private httpService: HttpService) {}

  async getSettings(): Promise<TradingSettings> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/Settings',
    };
    return await this.httpService.xhr(httpSetting);
  }

  async updateSettings(payload: Partial<TradingSettings>): Promise<TradingSettings> {
    const httpSetting: HttpSettings = {
      method: 'PUT',
      url: location.origin + '/api/Settings',
      data: payload
    };
    return await this.httpService.xhr(httpSetting);
  }
}

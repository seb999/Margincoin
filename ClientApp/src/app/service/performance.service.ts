import { Injectable } from '@angular/core';
import { HttpSettings, HttpService } from './http.service';

export interface Trade {
  symbol: string;
  profit: number;
  closeDate: string;
  closePrice: number;
  maxPotentialProfit: number;
}

export interface TradePerformance {
  totalTrades: number;
  winningTrades: number;
  losingTrades: number;
  totalGains: number;
  totalLosses: number;
  netProfit: number;
  bestTrade: number;
  worstTrade: number;
  trades: Trade[];
}

@Injectable({
  providedIn: 'root'
})
export class PerformanceService {
  constructor(private httpService: HttpService) {}

  async getPerformance(): Promise<TradePerformance> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/Performance',
    };
    return await this.httpService.xhr(httpSetting);
  }
}

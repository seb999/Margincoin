import { Injectable } from '@angular/core';
import { HttpSettings, HttpService } from './http.service';

// Static configuration from appsettings.json (read-only)
export interface StaticConfig {
  interval: string;
  maxCandle: string;
  numberOfSymbols: number;
  orderOffset: number;
  spotTickerTime: string;
  prevCandleCount: number;
  minConsecutiveUpSymbols: number;
  maxSpreadOverride: number;
  minAIScore: number;
  minPercentageUp: number;
  minRSI: number;
  maxRSI: number;
  minTrendScoreForEntry: number;
  trendScoreExitThreshold: number;
  useWeightedTrendScore: boolean;
  aiVetoConfidence: number;
  enableMLPredictions: boolean;
}

// Runtime settings from database (can be modified at runtime)
export interface RuntimeSettings {
  maxOpenTrades: number;
  quoteOrderQty: number;
  stopLossPercentage: number;
  takeProfitPercentage: number;
  timeBasedKillMinutes: number;
  enableAggressiveReplacement: boolean;
  surgeScoreThreshold: number;
  replacementScoreGap: number;
  replacementCooldownSeconds: number;
  maxReplacementsPerHour: number;
  maxCandidateDepth: number;
  weakTrendStopLossPercentage: number;
  enableDynamicStopLoss: boolean;
  trailingStopPercentage: number;
  enableMLPredictions: boolean;
  enableOpenAISignals: boolean;
}

// Combined settings response
export interface CombinedSettings {
  static: StaticConfig;
  runtime: RuntimeSettings;
}

// Legacy interface for backward compatibility
export interface TradingSettings extends RuntimeSettings {
  interval: string;
  numberOfSymbols: number;
  orderOffset: number;
  minPercentageUp: number;
  minRSI: number;
  maxRSI: number;
}

@Injectable({ providedIn: 'root' })
export class SettingsService {
  constructor(private httpService: HttpService) {}

  /**
   * Get static configuration (read-only from appsettings.json)
   */
  async getStaticConfig(): Promise<StaticConfig> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/Settings/static',
    };
    return await this.httpService.xhr(httpSetting);
  }

  /**
   * Get runtime settings (from database, can be modified)
   */
  async getRuntimeSettings(): Promise<RuntimeSettings> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/Settings/runtime',
    };
    return await this.httpService.xhr(httpSetting);
  }

  /**
   * Get combined settings (both static and runtime)
   */
  async getCombinedSettings(): Promise<CombinedSettings> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/Settings',
    };
    return await this.httpService.xhr(httpSetting);
  }

  /**
   * Update runtime settings only
   */
  async updateRuntimeSettings(payload: Partial<RuntimeSettings>): Promise<RuntimeSettings> {
    const httpSetting: HttpSettings = {
      method: 'PUT',
      url: location.origin + '/api/Settings/runtime',
      data: payload
    };
    return await this.httpService.xhr(httpSetting);
  }

  /**
   * Legacy method for backward compatibility
   * @deprecated Use getRuntimeSettings() instead
   */
  async getSettings(): Promise<TradingSettings> {
    const combined = await this.getCombinedSettings();
    return {
      ...combined.runtime,
      interval: combined.static.interval,
      numberOfSymbols: combined.static.numberOfSymbols,
      orderOffset: combined.static.orderOffset,
      minPercentageUp: combined.static.minPercentageUp,
      minRSI: combined.static.minRSI,
      maxRSI: combined.static.maxRSI,
    };
  }

  /**
   * Legacy method for backward compatibility
   * @deprecated Use updateRuntimeSettings() instead
   */
  async updateSettings(payload: Partial<TradingSettings>): Promise<TradingSettings> {
    const runtimePayload: Partial<RuntimeSettings> = {
      maxOpenTrades: payload.maxOpenTrades,
      quoteOrderQty: payload.quoteOrderQty,
      stopLossPercentage: payload.stopLossPercentage,
      takeProfitPercentage: payload.takeProfitPercentage,
      timeBasedKillMinutes: payload.timeBasedKillMinutes,
      enableAggressiveReplacement: payload.enableAggressiveReplacement,
      surgeScoreThreshold: payload.surgeScoreThreshold,
      replacementScoreGap: payload.replacementScoreGap,
      replacementCooldownSeconds: payload.replacementCooldownSeconds,
      maxReplacementsPerHour: payload.maxReplacementsPerHour,
      maxCandidateDepth: payload.maxCandidateDepth,
    };
    const updated = await this.updateRuntimeSettings(runtimePayload);
    return this.getSettings();
  }
}

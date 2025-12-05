import { Injectable } from '@angular/core';
import { HttpSettings, HttpService } from './http.service';

export interface AIPerformanceMetrics {
  totalOrders: number;
  profitableOrders: number;
  losingOrders: number;
  winRate: number;
  entryPredictions: {
    upPredictionAccuracy: number;
    upPredictionCount: number;
    upPredictionProfitable: number;
    downPredictionAccuracy: number;
    downPredictionCount: number;
    sidewaysPredictionAccuracy: number;
    sidewaysPredictionCount: number;
  };
  exitPredictions: {
    downExitAccuracy: number;
    downExitCount: number;
    upExitCount: number;
    sidewaysExitCount: number;
  };
  confidenceAnalysis: {
    highConfidenceEntry: ConfidenceMetric;
    mediumConfidenceEntry: ConfidenceMetric;
    lowConfidenceEntry: ConfidenceMetric;
    highConfidenceExit: ConfidenceMetric;
    mediumConfidenceExit: ConfidenceMetric;
    lowConfidenceExit: ConfidenceMetric;
  };
  profitByPrediction: ProfitByPrediction[];
  predictionChanges: PredictionChanges;
  performanceByDay: DailyPerformance[];
}

export interface ConfidenceMetric {
  count: number;
  accuracy: number;
  avgProfit: number;
  totalProfit?: number;
}

export interface ProfitByPrediction {
  prediction: string;
  totalProfit: number;
  avgProfit: number;
  count: number;
  winRate: number;
}

export interface PredictionChanges {
  totalWithExitData: number;
  upToDown?: {
    count: number;
    avgProfit: number;
  };
  upToUp?: {
    count: number;
    avgProfit: number;
  };
  upToSideways?: {
    count: number;
    avgProfit: number;
  };
}

export interface DailyPerformance {
  date: string;
  totalOrders: number;
  profitableOrders: number;
  totalProfit: number;
  avgEntryConfidence: number;
  avgExitConfidence: number;
}

export interface PredictionAccuracy {
  entryAccuracy: {
    upPrediction: AccuracyDetail;
  };
  exitAccuracy: {
    downPrediction: AccuracyDetail;
  };
  confidenceCorrelation: ConfidenceCorrelation[];
  symbolPerformance: SymbolPerformance[];
}

export interface AccuracyDetail {
  total: number;
  correct: number;
  incorrect: number;
  accuracy: number;
  avgProfitWhenCorrect: number;
  avgLossWhenIncorrect: number;
}

export interface ConfidenceCorrelation {
  confidenceLevel: number;
  count: number;
  accuracy: number;
  avgProfit: number;
}

export interface SymbolPerformance {
  symbol: string;
  totalOrders: number;
  accuracy: number;
  totalProfit: number;
  avgConfidence: number;
}

export interface PredictionVsActual {
  orderId: number;
  symbol: string;
  openDate: string;
  closeDate: string;
  entryPrediction: string;
  entryConfidence: number;
  exitPrediction: string;
  exitConfidence: number;
  profit: number;
  profitPercent: number;
  entryWasCorrect: boolean;
  exitWasCorrect: boolean | null;
  closeReason: string;
}

export interface AIRecommendations {
  analysisDate: string;
  daysPeriod: number;
  totalTrades: number;
  overallAccuracy: number;
  averageProfit: number;
  recommendations: string[];
  suggestedConfig: {
    enableAI: boolean;
    minAIScore: number;
    aiVetoConfidence: number;
  };
}

@Injectable({
  providedIn: 'root'
})
export class AIAnalyticsService {

  constructor(private httpService: HttpService) { }

  async getPerformanceMetrics(days: number = 30): Promise<AIPerformanceMetrics> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: `${location.origin}/api/AIAnalytics/PerformanceMetrics?days=${days}`,
    };
    return await this.httpService.xhr<AIPerformanceMetrics>(httpSetting);
  }

  async getPredictionAccuracy(days: number = 30): Promise<PredictionAccuracy> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: `${location.origin}/api/AIAnalytics/PredictionAccuracy?days=${days}`,
    };
    return await this.httpService.xhr<PredictionAccuracy>(httpSetting);
  }

  async getPredictionVsActual(limit: number = 50): Promise<PredictionVsActual[]> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: `${location.origin}/api/AIAnalytics/PredictionVsActual?limit=${limit}`,
    };
    return await this.httpService.xhr<PredictionVsActual[]>(httpSetting);
  }

  async getRecommendations(days: number = 30): Promise<AIRecommendations> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: `${location.origin}/api/AIAnalytics/Recommendations?days=${days}`,
    };
    return await this.httpService.xhr<AIRecommendations>(httpSetting);
  }
}

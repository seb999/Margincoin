export interface AiPrediction {
  symbol: string;
  pair?: string;
  prediction?: string;
  confidence?: number;
  expectedReturn?: number;
  upProbability?: number;
  sidewaysProbability?: number;
  downProbability?: number;
  trendScore?: number;
  timestamp?: string;
}

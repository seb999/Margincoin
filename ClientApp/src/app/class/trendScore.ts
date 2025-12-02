export interface TrendScore {
  symbol: string;      // base symbol (e.g., BTC)
  pair?: string;       // full pair (e.g., BTCUSDC)
  score: number;
}

import { Component, OnInit } from '@angular/core';
import {
  AIAnalyticsService,
  AIPerformanceMetrics,
  PredictionAccuracy,
  PredictionVsActual,
  AIRecommendations
} from '../service/ai-analytics.service';

@Component({
  selector: 'app-ai-analytics',
  templateUrl: './ai-analytics.component.html',
  styleUrls: ['./ai-analytics.component.css']
})
export class AIAnalyticsComponent implements OnInit {

  // Data
  performanceMetrics: AIPerformanceMetrics | null = null;
  predictionAccuracy: PredictionAccuracy | null = null;
  recentPredictions: PredictionVsActual[] = [];
  recommendations: AIRecommendations | null = null;

  // UI State
  isLoading = true;
  selectedPeriod = 30;
  selectedTab: 'overview' | 'accuracy' | 'predictions' | 'recommendations' = 'overview';
  errorMessage: string | null = null;

  // Period options
  periodOptions = [
    { value: 7, label: 'Last 7 days' },
    { value: 30, label: 'Last 30 days' },
    { value: 60, label: 'Last 60 days' },
    { value: 90, label: 'Last 90 days' }
  ];

  constructor(private analyticsService: AIAnalyticsService) { }

  async ngOnInit() {
    await this.loadAllData();
  }

  async loadAllData() {
    this.isLoading = true;
    this.errorMessage = null;

    try {
      await Promise.all([
        this.loadPerformanceMetrics(),
        this.loadPredictionAccuracy(),
        this.loadRecentPredictions(),
        this.loadRecommendations()
      ]);
    } catch (error: any) {
      console.error('Failed to load AI analytics:', error);
      this.errorMessage = error?.message || 'Failed to load analytics data';
    } finally {
      this.isLoading = false;
    }
  }

  async loadPerformanceMetrics() {
    try {
      this.performanceMetrics = await this.analyticsService.getPerformanceMetrics(this.selectedPeriod);
    } catch (error) {
      console.error('Failed to load performance metrics:', error);
    }
  }

  async loadPredictionAccuracy() {
    try {
      this.predictionAccuracy = await this.analyticsService.getPredictionAccuracy(this.selectedPeriod);
    } catch (error) {
      console.error('Failed to load prediction accuracy:', error);
    }
  }

  async loadRecentPredictions() {
    try {
      this.recentPredictions = await this.analyticsService.getPredictionVsActual(50);
    } catch (error) {
      console.error('Failed to load recent predictions:', error);
    }
  }

  async loadRecommendations() {
    try {
      this.recommendations = await this.analyticsService.getRecommendations(this.selectedPeriod);
    } catch (error) {
      console.error('Failed to load recommendations:', error);
    }
  }

  async onPeriodChange() {
    await this.loadAllData();
  }

  selectTab(tab: 'overview' | 'accuracy' | 'predictions' | 'recommendations') {
    this.selectedTab = tab;
  }

  // Helper methods for UI
  getAccuracyColor(accuracy: number): string {
    if (accuracy >= 0.6) return 'text-success';
    if (accuracy >= 0.5) return 'text-warning';
    return 'text-danger';
  }

  getAccuracyIcon(accuracy: number): string {
    if (accuracy >= 0.6) return '✅';
    if (accuracy >= 0.5) return '⚠️';
    return '❌';
  }

  getProfitColor(profit: number): string {
    return profit > 0 ? 'text-success' : 'text-danger';
  }

  formatPercent(value: number): string {
    return (value * 100).toFixed(1) + '%';
  }

  formatNumber(value: number, decimals: number = 2): string {
    return value?.toFixed(decimals) || '0.00';
  }

  getRecommendationClass(recommendation: string): string {
    if (recommendation.includes('✅') || recommendation.includes('GOOD')) return 'alert-success';
    if (recommendation.includes('⚠️') || recommendation.includes('MODERATE')) return 'alert-warning';
    if (recommendation.includes('❌') || recommendation.includes('POOR')) return 'alert-danger';
    return 'alert-info';
  }

  getConfidenceBadgeClass(confidence: number): string {
    if (confidence >= 0.7) return 'badge-success';
    if (confidence >= 0.5) return 'badge-warning';
    return 'badge-secondary';
  }

  getPredictionBadgeClass(prediction: string): string {
    if (prediction === 'up') return 'badge-success';
    if (prediction === 'down') return 'badge-danger';
    return 'badge-secondary';
  }

  getCorrectnessIcon(isCorrect: boolean | null): string {
    if (isCorrect === null) return '?';
    return isCorrect ? '✓' : '✗';
  }

  getCorrectnessClass(isCorrect: boolean | null): string {
    if (isCorrect === null) return 'text-muted';
    return isCorrect ? 'text-success' : 'text-danger';
  }
}

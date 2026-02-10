import { Component, OnInit } from '@angular/core';
import { PerformanceService, TradePerformance } from '../service/performance.service';
import * as Highcharts from 'highcharts';

@Component({
  selector: 'app-performance',
  templateUrl: './performance.component.html',
})
export class PerformanceComponent implements OnInit {
  performance: TradePerformance | null = null;
  loading = true;
  error: string = '';

  Highcharts: typeof Highcharts = Highcharts;
  chartOptions: Highcharts.Options = {};

  constructor(private performanceService: PerformanceService) {}

  async ngOnInit() {
    await this.loadPerformance();
  }

  async loadPerformance() {
    this.loading = true;
    this.error = '';
    try {
      this.performance = await this.performanceService.getPerformance();
      this.buildChart();
    } catch (err) {
      this.error = 'Failed to load performance data';
      console.error('Performance load error:', err);
    } finally {
      this.loading = false;
    }
  }

  buildChart() {
    if (!this.performance || !this.performance.trades.length) return;

    // For each trade, we show the max potential as the bar
    // and color-code how much was actually captured vs missed
    const actualProfitData = this.performance.trades.map((trade, index) => ({
      x: index + 1,
      y: trade.maxPotentialProfit, // Show the full potential height
      name: trade.symbol,
      color: '#FCD535',
      opacity: 0.3,
      actualProfit: trade.profit,
      maxPotential: trade.maxPotentialProfit
    }));

    // Overlay the actual profit achieved on top
    const actualCapturedData = this.performance.trades.map((trade, index) => ({
      x: index + 1,
      y: trade.profit,
      name: trade.symbol,
      color: trade.profit > 0 ? '#0ECB81' : '#F6465D',
      actualProfit: trade.profit,
      maxPotential: trade.maxPotentialProfit
    }));

    this.chartOptions = {
      chart: {
        type: 'column',
        backgroundColor: '#181A20',
        style: {
          fontFamily: 'inherit'
        }
      },
      title: {
        text: 'Individual Trade Performance',
        style: {
          color: '#EAECEF',
          fontSize: '14px',
          fontWeight: '600'
        }
      },
      xAxis: {
        title: {
          text: 'Trade #',
          style: { color: '#848E9C' }
        },
        labels: {
          style: { color: '#848E9C' }
        },
        gridLineColor: '#2B3139',
        lineColor: '#2B3139'
      },
      yAxis: {
        title: {
          text: 'Profit/Loss (USDC)',
          style: { color: '#848E9C' }
        },
        labels: {
          style: { color: '#848E9C' }
        },
        gridLineColor: '#2B3139',
        plotLines: [{
          value: 0,
          color: '#848E9C',
          width: 1,
          zIndex: 3
        }]
      },
      tooltip: {
        backgroundColor: '#0B0E11',
        borderColor: '#2B3139',
        style: {
          color: '#EAECEF'
        },
        shared: true,
        formatter: function() {
          const points: any = this.points;
          if (!points || points.length === 0) return '';

          const firstPoint = points[0].point;
          const actualProfit = firstPoint.actualProfit !== undefined ? firstPoint.actualProfit : firstPoint.y;
          const maxPotential = firstPoint.maxPotential || actualProfit;
          const missedProfit = maxPotential - actualProfit;

          let tooltip = `<b>${firstPoint.name || 'Trade'} #${this.x}</b><br/>`;
          tooltip += `Actual Profit: <b style="color:${actualProfit > 0 ? '#0ECB81' : '#F6465D'}">${actualProfit.toFixed(2)} USDC</b><br/>`;
          tooltip += `Max Potential: <b style="color:#FCD535">${maxPotential.toFixed(2)} USDC</b><br/>`;

          if (missedProfit > 0.01) {
            tooltip += `<span style="color:#848E9C">Missed Opportunity: ${missedProfit.toFixed(2)} USDC</span>`;
          }

          return tooltip;
        }
      },
      legend: {
        enabled: true,
        itemStyle: {
          color: '#EAECEF',
          fontSize: '12px'
        },
        itemHoverStyle: {
          color: '#FCD535'
        },
        backgroundColor: '#0B0E11',
        borderColor: '#2B3139',
        borderWidth: 1,
        borderRadius: 4,
        padding: 8
      },
      plotOptions: {
        column: {
          borderWidth: 0,
          groupPadding: 0.15,
          pointPadding: 0.1,
          grouping: false
        }
      },
      series: [{
        type: 'column',
        name: 'Max Potential Profit',
        data: actualProfitData,
        pointWidth: 20,
        zIndex: 1
      }, {
        type: 'column',
        name: 'Actual Profit/Loss',
        data: actualCapturedData,
        pointWidth: 20,
        zIndex: 2
      }],
      credits: {
        enabled: false
      }
    };
  }

  getWinRate(): number {
    if (!this.performance || this.performance.totalTrades === 0) return 0;
    return (this.performance.winningTrades / this.performance.totalTrades) * 100;
  }

  getAverageWin(): number {
    if (!this.performance || this.performance.winningTrades === 0) return 0;
    return this.performance.totalGains / this.performance.winningTrades;
  }

  getAverageLoss(): number {
    if (!this.performance || this.performance.losingTrades === 0) return 0;
    return this.performance.totalLosses / this.performance.losingTrades;
  }

  getProfitFactor(): number {
    if (!this.performance || this.performance.totalLosses === 0) return 0;
    return this.performance.totalGains / Math.abs(this.performance.totalLosses);
  }
}

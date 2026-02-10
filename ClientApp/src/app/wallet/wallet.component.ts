import { Component, OnDestroy, ViewChild, ViewEncapsulation } from '@angular/core';
import { NumericLiteral } from 'typescript';
import { Order } from '../class/order';
import { HttpSettings, HttpService } from '../service/http.service';
import { SignalRService } from '../service/signalR.service';
import { ServerMsg } from '../class/serverMsg';
import { NgbModal } from '@ng-bootstrap/ng-bootstrap';
import { NgbDateStruct } from '@ng-bootstrap/ng-bootstrap';
import { OrderDetailHelper } from './../orderDetail/orderDetail.helper';
import { BackEndMessage } from '../class/enum';

import * as Highcharts from 'highcharts';
import HC_HIGHSTOCK from 'highcharts/modules/stock';
import HC_INDIC from 'highcharts/indicators/indicators';
import HC_RSI from 'highcharts/indicators/rsi';
import HC_EMA from 'highcharts/indicators/ema';
import HC_MACD from 'highcharts/indicators/macd';
import HC_THEME from 'highcharts/themes/dark-unica';
import HC_exporting from "highcharts/modules/exporting";
import HC_exporting_offline from "highcharts/modules/offline-exporting";
import HC_Data from "highcharts/modules/export-data";
import { AppSetting } from '../app.settings';
import { BinanceOrder } from '../class/binanceOrder';
import { BinanceAccount } from '../class/binanceAccount';
import { TradeService } from '../service/trade.service';
import { AiPrediction } from '../class/aiPrediction';


HC_HIGHSTOCK(Highcharts);
HC_INDIC(Highcharts);
HC_RSI(Highcharts);
HC_EMA(Highcharts);
HC_MACD(Highcharts);
HC_THEME(Highcharts);
HC_exporting(Highcharts);
HC_Data(Highcharts);
HC_exporting_offline(Highcharts);

@Component({
  selector: 'app-wallet',
  templateUrl: './wallet.component.html',
  styleUrls: ['./wallet.component.css'],
  encapsulation: ViewEncapsulation.None,
})

export class WalletComponent implements OnDestroy {

  highcharts: typeof Highcharts = Highcharts;
  chartOptions: Highcharts.Options;
  // @ViewChild("chart", { static: false }) chart: any;
  @ViewChild('chart') componentRef;
  chartRef;
  updateFlag;


  model: NgbDateStruct;

  private ohlc = [] as any;
  public pendingOrderList: Order[];
  public myAccount: BinanceAccount;
  public symbolList: any[];
  public tradeSymbolList: any[];
  public symbolPrice: any[];
  public orderList: Order[] = [];
  public logList: any[];
  public CandleList: any[];
  public totalProfit: number;
  public totalBestProfit: number;
  public serverMsg: ServerMsg;
  public showMessageInfo: boolean = false;
  public showMessageError: boolean = false;
  public showMessageExport: boolean = false;
  public messageError: string;
  public messageExport: string;
  public interval: any;
  public intervalList = [] as any;
  public isTradeOpen: boolean;
  public popupSymbol: string;
  public popupSymbolPrice: number;
  public popupQty: number;
  public popupQuoteQty: number;
  public balance: number;
  public portfolioSurgeScores: { [symbol: string]: number } = {};
  public portfolioTrendScores: { [symbol: string]: number } = {};
  public portfolioAiSignals: { [symbol: string]: AiPrediction } = {};
  public isCollapsed = true;
  public slope: any;
  public isProd = false;
  public isMarketOrder = false;
  public color = 'accent';
  public displaySymbol: string;
  public myOrder: Order;
  public websocketStatus: any = null;
  public lastCandleUpdate: any = null;
  public showOnlyRunning: boolean = false;
  public currentOrderDetails: BinanceOrder | null = null;
  public currentOrderBuyType: string | null = null;
  public currentOrderSellType: string | null = null;
  public currentOrderTrendScore: number | null = null;
  public currentOrderAIScore: number | null = null;
  public currentOrderAIPrediction: string | null = null;
  public aiServiceHealthy: boolean = true;
  public activeSidePanel: 'chart' | 'portfolio' = 'portfolio';
  public portfolioSurgeScoreFilter: number | null = null;
  private lastStreamAt: number = Date.now();
  private streamWatchdog: any;
  private readonly streamStaleThresholdMs = 30000;
  private aiHealthTimer: any;
  private readonly aiHealthIntervalMs = 30000;

  get filteredOrderList(): Order[] {
    if (!this.orderList) return [];
    if (this.showOnlyRunning) {
      return this.orderList.filter(order => order.isClosed === 0 || !order.isClosed);
    }
    return this.orderList;
  }

  get filteredPortfolioBalances() {
    if (!this.myAccount?.balances) return [];
    if (this.portfolioSurgeScoreFilter === null) return this.myAccount.balances;

    return this.myAccount.balances.filter(asset => {
      const surgeScore = this.getSurgeScoreKey(asset.asset);
      if (surgeScore === undefined) return false;
      return surgeScore >= this.portfolioSurgeScoreFilter!;
    });
  }

  constructor(
    public modalService: NgbModal,
    private httpService: HttpService,
    private tradeService: TradeService,
    private signalRService: SignalRService,
    private orderDetailHelper: OrderDetailHelper,
    private appSetting: AppSetting,
  ) {
    this.intervalList = this.appSetting.intervalList;
  }

  async ngOnInit() {
    this.model = this.today();
    this.isTradeOpen = false;

    //Open listener on my API SignalR - must be before API calls to catch errors
    await this.signalRService.startConnection();
    this.signalRService.openDataListener();

    this.startStreamWatchdog();
    this.startAiHealthWatchdog();

    this.signalRService.onMessage().subscribe(async message => {
      this.serverMsg = message;
      if (this.serverMsg.msgName == BackEndMessage.trading) {
        this.isTradeOpen = true;
        this.markStreamHeartbeat();
        this.showMessageInfo = true;
        this.CandleList = this.serverMsg.candleList;
        if (this.orderList?.length > 0 && this.CandleList?.length > 0) {
          this.orderList.forEach((order, index) => {
            this.CandleList.forEach(candle => {
              if (order.symbol === candle.s && !order.isClosed) {
                this.orderList[index].closePrice = candle.c;
                this.orderList[index].profit = (candle.c - order.openPrice) * order.quantityBuy;
              }
            });
          });
        }
        this.calculateProfit();
        setTimeout(() => { this.showMessageInfo = false }, 700);
      }

      if (this.serverMsg.msgName == BackEndMessage.newPendingOrder) {
        // Order notification removed - use modal from datagrid instead
      }

      if (this.serverMsg.msgName == BackEndMessage.refreshUI) {
        this.orderList = await this.tradeService.getAllOrder(this.model.day + "-" + this.model.month + "-" + this.model.year);
        this.calculateProfit();
        this.refreshUI();
      }

      if (this.serverMsg.msgName == BackEndMessage.sellOrderFilled
        || this.serverMsg.msgName == BackEndMessage.buyOrderFilled) {
        this.orderList = await this.tradeService.getAllOrder(this.model.day + "-" + this.model.month + "-" + this.model.year);
        // Order notification removed - use modal from datagrid instead
        this.calculateProfit();
      }

      if (this.serverMsg.msgName == BackEndMessage.exportChart) {
        this.showMessageExport= true;
        this.messageExport = "Exporting charts...";
        this.symbolList = this.serverMsg.tradeSymbolList;
        this.exportChart();
      }

      if (this.serverMsg.msgName == BackEndMessage.webSocketStopped) {
        this.isTradeOpen = true;
        this.trade();
      }

      if (this.serverMsg.msgName == BackEndMessage.httpRequestError) {
        this.showMessageError = true;
        this.messageError = this.serverMsg.httpError;
        setTimeout(() => { this.showMessageError = false }, 5000);
      }

      if (this.serverMsg.msgName == BackEndMessage.accessFaulty) {
        this.showMessageError = true;
        this.messageError = 'Binance API access error - check API keys';
        setTimeout(() => { this.showMessageError = false }, 10000);
      }

      if (this.serverMsg.msgName == BackEndMessage.badRequest) {
        this.showMessageError = true;
        this.messageError = 'Bad request to Binance API';
        setTimeout(() => { this.showMessageError = false }, 5000);
      }

      if (this.serverMsg.msgName == BackEndMessage.websocketStatus) {
        try {
          this.websocketStatus = typeof this.serverMsg.data === 'string'
            ? JSON.parse(this.serverMsg.data)
            : this.serverMsg.data;
        } catch {
          this.websocketStatus = null;
        }
        this.markStreamHeartbeat();
      }

      if (this.serverMsg.msgName == BackEndMessage.candleUpdate) {
        this.lastCandleUpdate = JSON.parse(this.serverMsg.data);
        this.markStreamHeartbeat();
        setTimeout(() => { this.lastCandleUpdate = null }, 2000);
      }
    });

    // Load data after SignalR is set up to catch any errors
    this.isProd = await this.tradeService.getActiveServer();
    this.interval = await this.tradeService.getInterval();
    this.myAccount = await this.tradeService.binanceAccount();
    this.symbolPrice = await this.tradeService.getSymbolPrice();
    this.portfolioSurgeScores = await this.tradeService.getSurgeScoresForBalances();
    this.portfolioTrendScores = await this.tradeService.getTrendScoresForBalances();
    await this.loadAiSignals();
    this.orderList = await this.tradeService.getAllOrder(this.model.day + "-" + this.model.month + "-" + this.model.year);

    this.calculateProfit();
    this.calculateBalance();
  }

  async refreshUI() {
    this.myAccount = await this.tradeService.binanceAccount();
    this.logList = await this.tradeService.getLog();
    this.portfolioSurgeScores = await this.tradeService.getSurgeScoresForBalances();
    this.portfolioTrendScores = await this.tradeService.getTrendScoresForBalances();
    await this.loadAiSignals();
  }

  async refreshOrderTable() {
    this.orderList = await this.tradeService.getAllOrder(this.model.day + "-" + this.model.month + "-" + this.model.year);
  }

  ngOnDestroy(): void {
    if (this.streamWatchdog) {
      clearInterval(this.streamWatchdog);
    }
    if (this.aiHealthTimer) {
      clearInterval(this.aiHealthTimer);
    }
  }

  private setDecisionTypes(reason?: string, side?: string) {
    const trimmed = reason?.trim() || null;
    if (!side) {
      this.currentOrderBuyType = trimmed;
      this.currentOrderSellType = trimmed;
    } else if (side === 'BUY') {
      this.currentOrderBuyType = trimmed;
    } else if (side === 'SELL') {
      this.currentOrderSellType = trimmed;
    }
  }

  private startStreamWatchdog() {
    this.streamWatchdog = setInterval(() => {
      const elapsed = Date.now() - this.lastStreamAt;
      if (elapsed > this.streamStaleThresholdMs) {
        this.showMessageError = true;
        this.messageError = `No market data received for ${Math.floor(elapsed / 1000)}s. Check backend stream or websocket connection.`;
      }
    }, 5000);
  }

  private markStreamHeartbeat() {
    this.lastStreamAt = Date.now();
    if (this.showMessageError && this.messageError?.includes('No market data received')) {
      this.showMessageError = false;
    }
  }

  private startAiHealthWatchdog() {
    this.checkAiHealth();
    this.aiHealthTimer = setInterval(() => this.checkAiHealth(), this.aiHealthIntervalMs);
  }

  private async checkAiHealth() {
    try {
      this.aiServiceHealthy = await this.tradeService.getAiHealth();
    } catch {
      this.aiServiceHealthy = false;
    }
  }

  private findOrderReason(binanceOrder: BinanceOrder | null): string | undefined {
    if (!binanceOrder) return undefined;
    const orderIdNum = Number(binanceOrder.orderId);
    const match = this.orderList?.find(o => {
      const buyId = o.buyOrderId != null ? Number(o.buyOrderId) : null;
      const sellId = o.sellOrderId != null ? Number(o.sellOrderId) : null;
      return (buyId != null && !isNaN(orderIdNum) && buyId === orderIdNum) ||
        (sellId != null && !isNaN(orderIdNum) && sellId === orderIdNum) ||
        o.symbol === binanceOrder.symbol;
    });
    return match?.type;
  }

  private findOrderTrendScore(symbol: string, orderId?: number): number | null {
    if (!this.orderList?.length) return null;
    const orderIdNum = orderId != null ? Number(orderId) : null;
    const match = this.orderList.find(o => {
      const buyId = o.buyOrderId != null ? Number(o.buyOrderId) : null;
      const sellId = o.sellOrderId != null ? Number(o.sellOrderId) : null;
      return (orderIdNum != null && !isNaN(orderIdNum) && (buyId === orderIdNum || sellId === orderIdNum)) ||
        o.symbol === symbol;
    });
    return match?.trendScore != null ? Number(match.trendScore) : null;
  }

  private findOrderAIData(symbol: string, orderId?: number): { score: number | null, prediction: string | null } {
    if (!this.orderList?.length) return { score: null, prediction: null };
    const orderIdNum = orderId != null ? Number(orderId) : null;
    const match = this.orderList.find(o => {
      const buyId = o.buyOrderId != null ? Number(o.buyOrderId) : null;
      const sellId = o.sellOrderId != null ? Number(o.sellOrderId) : null;
      return (orderIdNum != null && !isNaN(orderIdNum) && (buyId === orderIdNum || sellId === orderIdNum)) ||
        o.symbol === symbol;
    });
    return {
      score: match?.aiScore != null ? Number(match.aiScore) : null,
      prediction: match?.aiPrediction ?? null
    };
  }

  formatAIScore(score: number | null, prediction: string | null): string {
    if (score == null || !prediction || score === 0) return '-';
    const arrow = prediction === 'up' ? '↑' : (prediction === 'down' ? '↓' : '•');
    return `${arrow}${(score * 100).toFixed(1)}%`;
  }

  getAIScoreClass(score: number | null, prediction: string | null): string {
    if (score == null || !prediction || score === 0) return 'text-gray-400';
    return prediction === 'up' ? 'text-emerald-400' : (prediction === 'down' ? 'text-red-400' : 'text-amber-400');
  }

  async openOrderDetailsModal(orderDetailTemplate, orderId: string, symbol: string) {
    this.currentOrderDetails = null;
    this.currentOrderBuyType = null;
    this.currentOrderSellType = null;
    this.currentOrderTrendScore = null;
    this.currentOrderAIScore = null;
    this.currentOrderAIPrediction = null;

    const orderIdNum = parseFloat(orderId);
    if (orderId && orderIdNum > 0) {
      const orderDetails = await this.tradeService.getOrderStatus(symbol, orderIdNum);
      if (orderDetails) {
        this.currentOrderDetails = orderDetails;
        const decisionType = this.findOrderReason(orderDetails);
        this.setDecisionTypes(decisionType, orderDetails.side);
        this.currentOrderTrendScore = this.findOrderTrendScore(orderDetails.symbol, orderDetails.orderId);
        const aiData = this.findOrderAIData(orderDetails.symbol, orderDetails.orderId);
        this.currentOrderAIScore = aiData.score;
        this.currentOrderAIPrediction = aiData.prediction;

        this.modalService.open(orderDetailTemplate, {
          ariaLabelledBy: 'order-detail-modal',
          size: 'md',
          centered: true
        });
      }
    }
  }

  today() {
    var d = new Date();
    return { day: d.getDate(), month: d.getMonth() + 1, year: d.getFullYear() };
  }

  async filterOrderList() {
    if (this.model == undefined) {
      return;
    }
    this.orderList = await this.tradeService.getAllOrder(this.model.day + "-" + this.model.month + "-" + this.model.year);
    this.calculateProfit();
  }

  toggleTradeFilter() {
    this.showOnlyRunning = !this.showOnlyRunning;
  }

  toggleSurgeScoreFilter(threshold: number) {
    if (this.portfolioSurgeScoreFilter === threshold) {
      this.portfolioSurgeScoreFilter = null;
    } else {
      this.portfolioSurgeScoreFilter = threshold;
    }
  }

  async changeTradeServer() {
    await this.tradeService.setServer(this.isProd);
    //this.assetList = await this.binanceAsset();
    this.myAccount = await this.tradeService.binanceAccount();
  }

  async changeOrderType() {
    await this.tradeService.setOrderType(this.isMarketOrder);
  }

  calculateProfit() {
    this.totalProfit = 0;
    if (this.orderList?.length > 0) {
      for (let i = 0; i < this.orderList.length; i++) {
        this.totalProfit += this.orderList[i].profit;
      }
    }

    this.totalBestProfit = 0;
    if (this.orderList?.length > 0) {
      this.totalBestProfit = this.orderList.map(a => (a.highPrice - a.openPrice) * a.quantityBuy).reduce(function (a, b) {
        if (a != 0) return a + b;
      });
    }
  }

  calculateBalance() {
    if (!this.myAccount?.balances?.length || !this.symbolPrice?.length) {
      return;
    }
    let balance = 0;
    for (const asset of this.myAccount.balances) {
      if (asset.asset === 'USDC') {
        balance += parseFloat(asset.free);
      } else {
        const price = this.symbolPrice.find(p => p.symbol === asset.asset + 'USDC');
        if (price) {
          balance += parseFloat(price.price) * parseFloat(asset.free);
        }
      }
    }
    this.balance = balance;
  }

  formatBalance(value: string, asset: string): string {
    const num = parseFloat(value);
    if (num === 0) return '-';
    if (asset === 'USDC') return num.toFixed(2);
    if (num >= 1) return num.toFixed(4);
    if (num >= 0.0001) return num.toFixed(6);
    return num.toFixed(8);
  }

  async trade(): Promise<any> {
    this.isTradeOpen = true;
    this.tradeService.setTradeParam(this.isTradeOpen);
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/AlgoTrade/MonitorMarket',
    };
    return await this.httpService.xhr(httpSetting);
  }

  async stopTrade(): Promise<any> {
    this.isTradeOpen = !this.isTradeOpen;
    this.tradeService.setTradeParam(this.isTradeOpen);
  }
  
  async closeTrade(id, lastPrice): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/AlgoTrade/CloseTrade/' + id + "/" + lastPrice,
    };
    return await this.httpService.xhr(httpSetting);
  }

  debugBuy(){
    this.tradeService.debugBuy();
  }

  openSeq() {
    window.open('http://localhost:5341', '_blank');
  }

  async syncBinanceSymbol(): Promise<any> {
    await this.tradeService.syncBinanceSymbol();
    await this.refreshUI();
  }

  // Removed - replaced with openOrderDetailsModal

  async cancelSymbolOrder(symbol): Promise<any> {
    this.tradeService.cancelSymbolOrder(symbol);
  }

  /////////////////////////////////////////////////////////////
  /////////////       Popup Sell / Buy    //////////////////////
  /////////////////////////////////////////////////////////////

  openPopup(popupTemplate, symbol, availableQty) {
    this.popupSymbol = symbol;
    this.popupQty = parseFloat(availableQty) || 0;

    // get symbol price
    this.popupSymbolPrice = this.getSymbolPriceValue(symbol) ?? 0;
    this.popupQuoteQty = this.popupQty && this.popupSymbolPrice ? this.popupQty * this.popupSymbolPrice : 0;

    this.modalService.open(popupTemplate, { ariaLabelledBy: 'modal-basic-title', size: 'lg' }).result.then((result) => {
      if (result == 'sell') {
        this.tradeService.sell(symbol, this.popupQty);
      }
      if (result == 'buy') {
        this.tradeService.buy(symbol, this.popupQuoteQty);
      }
    }, (reason) => { });
  }

  onPopupQtyChange(){
    if (this.popupQty && this.popupSymbolPrice) {
      this.popupQuoteQty = this.popupQty * this.popupSymbolPrice;
    } else {
      this.popupQuoteQty = 0;
    }
  }
  onPopupQuoteQtyChange(){
    if (this.popupSymbolPrice) {
      this.popupQty = this.popupQuoteQty / this.popupSymbolPrice;
    } else {
      this.popupQty = 0;
    }
  }

  private resolveSymbolPair(symbol: string): string {
    const upper = (symbol || '').toUpperCase();
    if (upper.endsWith('USDC') || upper.endsWith('USDT')) return upper;
    const hasUsdc = this.symbolPrice?.some(p => p.symbol === `${upper}USDC`);
    if (hasUsdc) return `${upper}USDC`;
    const hasUsdt = this.symbolPrice?.some(p => p.symbol === `${upper}USDT`);
    if (hasUsdt) return `${upper}USDT`;
    return `${upper}USDC`;
  }

  private getSymbolPriceValue(symbol: string): number | null {
    if (!this.symbolPrice?.length) return null;
    const upper = symbol?.toUpperCase();
    const matchUsdc = this.symbolPrice.find((crypto) => crypto.symbol === `${upper}USDC`);
    if (matchUsdc) return parseFloat(matchUsdc.price);
    const fallbackUsdt = this.symbolPrice.find((crypto) => crypto.symbol === `${upper}USDT`);
    if (fallbackUsdt) return parseFloat(fallbackUsdt.price);
    const looseMatch = this.symbolPrice.find((crypto) => crypto.symbol.startsWith(upper));
    return looseMatch ? parseFloat(looseMatch.price) : null;
  }

  getAssetValue(symbol: string, amount: string | number): number {
    const qty = parseFloat(amount as any);
    if (isNaN(qty) || qty === 0) return 0;
    const price = this.getSymbolPriceValue(symbol);
    if (!price) return 0;
    return qty * price;
  }

  getTrendScoreKey(symbol: string): number | undefined {
    if (!this.portfolioTrendScores) return undefined;
    const upper = (symbol || '').toUpperCase();
    return this.portfolioTrendScores[upper];
  }

  getSurgeScoreKey(symbol: string): number | undefined {
    if (!this.portfolioSurgeScores) return undefined;
    const upper = (symbol || '').toUpperCase();
    return this.portfolioSurgeScores[upper];
  }

  getTrendScoreBadgeClass(trendScore: number | undefined): string {
    if (trendScore === undefined || trendScore === null) return 'bg-gray-500/20 text-gray-300';
    // Red background if trend score is below the minimum entry threshold (3)
    if (trendScore < 3) return 'bg-red-500/20 text-red-300';
    // Green background if trend score meets or exceeds the minimum entry threshold
    return 'bg-emerald-500/20 text-emerald-300';
  }

  getSurgeScoreBadgeClass(score: number | undefined): string {
    if (score === undefined || score === null) return 'bg-gray-500/20 text-gray-300';
    if (score < 0.5) return 'bg-red-500/20 text-red-300';
    if (score < 2) return 'bg-amber-500/20 text-amber-300';
    return 'bg-emerald-500/20 text-emerald-300';
  }

  private async loadAiSignals() {
    try {
      const signals = await this.tradeService.getAiSignalsForBalances();
      const hasRealPredictions = Object.values(signals || {}).some(p =>
        p &&
        p.trendScore != null &&
        p.confidence != null
      );
      if (hasRealPredictions) {
        this.portfolioAiSignals = signals;
        this.aiServiceHealthy = true;
      } else {
        this.portfolioAiSignals = {};
        this.aiServiceHealthy = false;
      }
    } catch {
      this.portfolioAiSignals = {};
      this.aiServiceHealthy = false;
    }
  }

  getAiPrediction(symbol: string): AiPrediction | undefined {
    if (!this.portfolioAiSignals) return undefined;
    const upper = (symbol || '').toUpperCase();
    return this.portfolioAiSignals[upper];
  }

  getAiBadgeClass(prediction?: string): string {
    const direction = (prediction || '').toLowerCase();
    if (direction === 'up') return 'bg-emerald-500/20 text-emerald-300';
    if (direction === 'down') return 'bg-red-500/20 text-red-300';
    return 'bg-sky-500/20 text-sky-200';
  }

  formatAiBadge(pred?: AiPrediction): string {
    if (!this.aiServiceHealthy || !pred || pred.confidence == null || pred.trendScore == null) return '-';
    const direction = (pred.prediction || '').toLowerCase();
    const arrow = direction === 'up' ? '↑' : direction === 'down' ? '↓' : '→';
    const confidence = this.getAiConfidence(direction, pred);
    const percent = confidence != null ? Math.round(confidence * 100) : null;
    return percent !== null ? `${arrow}${percent}%` : arrow;
  }

  private getAiConfidence(direction: string, pred: AiPrediction): number | null {
    if (pred?.confidence != null) return pred.confidence;
    if (direction === 'up' && pred?.upProbability != null) return pred.upProbability;
    if (direction === 'down' && pred?.downProbability != null) return pred.downProbability;
    if (direction === 'sideways' && pred?.sidewaysProbability != null) return pred.sidewaysProbability;
    return null;
  }

  /////////////////////////////////////////////////////////////
  /////////////    HighChart methods     //////////////////////
  /////////////////////////////////////////////////////////////

  async exportChart() {
    for (var i = 0; i < this.symbolList?.length; i++) {
      await new Promise(next => {
        this.displaySymbol = this.symbolList[i].SymbolName;
        this.displayHighstock();
        setTimeout(() => {
          this.componentRef.chart.exportChartLocal({
            type: "image/jpeg",
            filename: this.displaySymbol,
          });
          next("d");
        }, 3000);
      });
    }
    setTimeout(() => {
      this.showMessageExport = false
      this.tradeService.mlUpdate();
    }, 2000);
  }

  async showChart(symbol, orderId) {
    this.activeSidePanel = 'chart';
    if (orderId != null) {
      this.displaySymbol = symbol;
      this.myOrder = await this.orderDetailHelper.getOrder(orderId);
    }
    else {
      this.displaySymbol = this.resolveSymbolPair(symbol);
      this.myOrder = null;
    }

    this.slope = await this.tradeService.getSymbolMacdSlope(this.displaySymbol);
    this.displayHighstock();
  }

  chartCallback: Highcharts.ChartCallbackFunction = (chart) => {
    this.chartRef = chart;
  };

  clearChart() {
    this.chartRef.destroy();
    this.componentRef.chart = null;
    this.updateFlag = true;
  }

  getOpenDateTimeSpam(openDate) {
    if (openDate != null) {
      var openDateArr = openDate.split(" ")[0].split("/");
      var openTime = openDate.split(" ")[1];
      return Date.parse(openDateArr[2] + "/" + openDateArr[1] + "/" + openDateArr[0] + " " + openTime);
    }
  }

  async changeHighstockResolution(key) {
    this.interval = key
    this.displayHighstock();
  }

  async displayHighstock() {
    let chartHeight = '';
    this.ohlc = null;
    this.ohlc = await this.orderDetailHelper.getIntradayData(this.displaySymbol, this.interval, 60);

    let chartData = [] as any;
    let volume = [] as any;

    this.ohlc.map((data, index) => {
      chartData.push([
        parseFloat(data[0]),
        parseFloat(data[1]),
        parseFloat(data[2]),
        parseFloat(data[3]),
        parseFloat(data[4]),
      ]),
        volume.push([
          parseFloat(data[0]),
          parseFloat(data[5]),
        ])
    });

    // var macdSlopeP1 = [this.slope.p1.x, this.slope.p1.y ];
    // var macdSlopeP2 =  [this.slope.p2.x, this.slope.p2.y ];

    const pricePlotLines: Highcharts.YAxisPlotLinesOptions[] = [];
    if (this.myOrder?.openPrice) {
      pricePlotLines.push({
        color: '#5CE25C',
        width: 1,
        value: this.myOrder.openPrice,
        label: {
          text: 'Entry',
          align: 'right',
          y: -4,
          style: { color: '#5CE25C', backgroundColor: 'rgba(92,226,92,0.1)', padding: '2px', borderRadius: 2 }
        }
      });
    }
    if (this.myOrder?.closePrice) {
      pricePlotLines.push({
        color: '#F59E0B',
        width: 1,
        value: this.myOrder.closePrice,
        label: {
          text: 'Exit',
          align: 'left',
          y: -4,
          style: { color: '#F59E0B', backgroundColor: 'rgba(245,158,11,0.08)', padding: '2px', borderRadius: 2 }
        }
      });
    }

    this.chartOptions = {
      tooltip: {
        shared: true,
        backgroundColor: '#0f172a',
        borderColor: '#1f2937',
        style: { color: '#e5e7eb' }
      },
      plotOptions: {

        //   macd: {   there is a bug here, second drowing not working
        //     zones: [{
        //         value: 0,
        //         color: '#cb585f'
        //     }, {
        //         color: '#41c9ad'
        //     }]
        // },
        candlestick: {
          upColor: '#41c9ad',
          color: '#cb585f',
          upLineColor: '#41c9ad',
          lineColor: '#cb585f',
        },
      },
      xAxis: {
        labels: { y: -2 },
        showLastLabel: false,
        gridLineWidth: 0,
        plotLines: [{
          color: 'green',
          width: 1,
          value: this.getOpenDateTimeSpam(this.myOrder?.openDate),  //display openeing date
        },
        {
          color: 'green',
          width: 1,
          value: this.getOpenDateTimeSpam(this.myOrder?.closeDate),  //display openeing date
        }]
      },
      yAxis:
        [
          {
            crosshair: false,
            labels: {
              align: 'right',
              x: -8
            },
            height: '60%',
            opposite: true,
            gridLineWidth: 0.5,
            gridLineColor: '#2e3544',
            plotLines: pricePlotLines,
          },
          {
            minorTickInterval: null,
            labels: { align: 'left', enabled: true, style: { color: '#9ca3af', fontSize: '9px' } },
            top: '60%',
            height: '40%',
            title: { text: '', style: { color: '#9ca3af', fontSize: '10px' } },
            gridLineWidth: 0,
          }
        ],
    }

    this.chartOptions.series =
      [
        {
          data: chartData,
          type: 'candlestick',
          yAxis: 0,
          id: 'quote',
          name: 'quote'
        },
        {
          type: 'macd',
          yAxis: 1,
          linkedTo: 'quote',
          name: 'MACD',
          macdLine: {
            zones: [{
              color: '#41c9ad'
            }]
          },
          signalLine: {
            zones: [{
              color: 'orange'
            }]
          }
        }
      ]
  }

}

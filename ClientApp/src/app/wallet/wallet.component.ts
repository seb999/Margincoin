import { Component, ViewChild, ViewEncapsulation } from '@angular/core';
import { NumericLiteral } from 'typescript';
import { Order } from '../class/order';
import { HttpSettings, HttpService } from '../service/http.service';
import { SignalRService } from '../service/signalR.service';
import { ServerMsg } from '../class/serverMsg';
import { NgbModal } from '@ng-bootstrap/ng-bootstrap';
import { NgbDateStruct, NgbPopover } from '@ng-bootstrap/ng-bootstrap';
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
import { FindValueSubscriber } from 'rxjs/internal/operators/find';
import { BinanceAccount } from '../class/binanceAccount';


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

export class WalletComponent {

  highcharts: typeof Highcharts = Highcharts;
  chartOptions: Highcharts.Options;
  // @ViewChild("chart", { static: false }) chart: any;
  @ViewChild('chart') componentRef;
  chartRef;
  updateFlag;


  model: NgbDateStruct;

  @ViewChild('p1') orderPopOver: any;

  private ohlc = [] as any;
  public pendingOrderList: Order[];
  public myAccount: BinanceAccount;
  public symbolList: any[];
  public tradeSymbolList: any[];
  public symbolPrice: any[];
  public orderList: Order[];
  public logList: any[];
  public CandleList: any[];
  public totalProfit: number;
  public totalBestProfit: number;
  public serverMsg: ServerMsg;
  public showMessageInfo: boolean = false;
  public showMessageError: boolean = false;
  public messageError: string;
  public interval: any;
  public intervalList = [] as any;
  public tradeOpen: boolean;
  public popupSymbol: string;
  public popupSymbolPrice: number;
  public popupQty: number;
  public popupQuoteQty: number;
  public balance: number;
  public isCollapsed = true;
  public slope: any;
  public isProd = false;
  public color = 'accent';
  public displaySymbol: string;
  public myOrder: Order;

  constructor(
    public modalService: NgbModal,
    private httpService: HttpService,
    private signalRService: SignalRService,
    private orderDetailHelper: OrderDetailHelper,
    private appSetting: AppSetting,
  ) {
    this.intervalList = this.appSetting.intervalList;
    //this.interval = "30m";
  }

  async ngOnInit() {
    this.tradeOpen = false;
   
    this.model = this.today();
    this.orderList = await this.getAllOrder(this.model.day + "-" + this.model.month + "-" + this.model.year);
    this.isProd = await this.getActiveServer();

    this.symbolList = await this.getSymbolList();
    this.symbolPrice = await this.getSymbolPrice();
    this.logList = await this.getLog();
    this.myAccount = await this.binanceAccount();
    this.interval = await this.getInterval();
   
    this.calculateProfit();
    this.calculateBalance();

    //Open listener on my API SignalR
    this.signalRService.startConnection();
    this.signalRService.openDataListener();

    this.signalRService.onMessage().subscribe(async message => {
      this.serverMsg = message;
      if (this.serverMsg.msgName == BackEndMessage.trading) {
        this.tradeOpen = true;
        this.showMessageInfo = true;
        this.CandleList = this.serverMsg.candleList;
        this.orderList.forEach((order, index) => {
          this.CandleList.forEach(candle => {
            if (order.symbol === candle.s && !order.isClosed) {
              this.orderList[index].closePrice = candle.c;
              this.orderList[index].profit = (candle.c - order.openPrice) * order.quantityBuy;
            }
          });
        });
        this.calculateProfit();
        setTimeout(() => { this.showMessageInfo = false }, 700);
      }

      if (this.serverMsg.msgName == BackEndMessage.newPendingOrder) {
        let binanceOrder = this.serverMsg.order;
        if (this.orderPopOver.isOpen()) {
          setTimeout(() => { this.openPopOver(this.orderPopOver, binanceOrder); }, 7000);
          setTimeout(() => { this.closePopOver(this.orderPopOver); }, 11000);
        }
        else {
          this.openPopOver(this.orderPopOver, binanceOrder);
          setTimeout(() => { this.closePopOver(this.orderPopOver); }, 7000);
        }
      }

      if (this.serverMsg.msgName == BackEndMessage.newOrder) {
        this.orderList = await this.getAllOrder(this.model.day + "-" + this.model.month + "-" + this.model.year);
        this.calculateProfit();
        this.refreshUI();
      }

      if (this.serverMsg.msgName == BackEndMessage.sellOrderFilled 
        || this.serverMsg.msgName == BackEndMessage.buyOrderFilled) {
        this.orderList = await this.getAllOrder(this.model.day + "-" + this.model.month + "-" + this.model.year);
        let binanceOrder = this.serverMsg.order;
        this.openPopOver(this.orderPopOver, binanceOrder);
        setTimeout(() => {
          this.closePopOver(this.orderPopOver);
          this.refreshUI();
        }, 7000);
      }

      if (this.serverMsg.msgName == BackEndMessage.exportChart) {
        this.showMessageError = true;
        this.messageError = "Exporting charts...";
        this.symbolList = this.serverMsg.tradeSymbolList;
        this.exportChart();
      }

      if (this.serverMsg.msgName == BackEndMessage.webSocketStopped) {
        this.trade();
      }

      if (this.serverMsg.msgName == BackEndMessage.httpRequestError) {
        this.showMessageError = true;
        this.messageError = this.serverMsg.httpError;
        setTimeout(() => { this.showMessageError = false }, 5000);
      }
    });
  }

  async refreshUI() {
    this.myAccount = await this.binanceAccount();
    this.logList = await this.getLog();
  }

  async refreshOrderTable() {
    this.orderList = await this.getAllOrder(this.model.day + "-" + this.model.month + "-" + this.model.year);
  }

  openPopOver(popover, order: BinanceOrder) {
    popover.open({ order });
  }

  closePopOver(popover) {
    if (popover.isOpen()) popover.close();
  }

  today() {
    var d = new Date();
    return { day: d.getDate(), month: d.getMonth() + 1, year: d.getFullYear() };
  }

  async filterOrderList() {
    if (this.model == undefined) {
      return;
    }
    this.orderList = await this.getAllOrder(this.model.day + "-" + this.model.month + "-" + this.model.year);
    this.calculateProfit();
  }

  async changeTradeServer() {
    await this.setServer(this.isProd);
    //this.assetList = await this.binanceAsset();
    this.myAccount = await this.binanceAccount();
  }

  calculateProfit() {
    this.totalProfit = 0;
    if (this.orderList.length > 0) {
      for (let i = 0; i < this.orderList.length; i++) {
        this.totalProfit += this.orderList[i].profit;
      }
    }

    this.totalBestProfit = 0;
    if (this.orderList.length > 0) {
      this.totalBestProfit = this.orderList.map(a => (a.highPrice - a.openPrice) * a.quantityBuy).reduce(function (a, b) {
        if (a != 0) return a + b;
      });
    }
  }

  calculateBalance() {
    let balance = 0;
    for (let i = 0; i <= this.symbolList.length; i++) {
      let index = this.symbolPrice.findIndex((crypto) => crypto.symbol === this.myAccount.balances[i].asset + "USDT");
      if (index !== -1) {
        balance += parseFloat(this.symbolPrice[index].price) * this.myAccount.balances[i].free;
      }
    }
    this.balance = balance;
  }

  async stopTrade(): Promise<any> {
    this.tradeOpen = !this.tradeOpen;
    this.setTradeParam();
  }

  /////////////////////////////////////////////////////////////
  /////////////       Popup Sell / Buy    //////////////////////
  /////////////////////////////////////////////////////////////

  openPopup(popupTemplate, symbol, availableQty) {
    this.popupSymbol = symbol;
    this.popupQty = availableQty;

    // get symbol price
    let index = this.symbolPrice.findIndex((crypto) => crypto.symbol === symbol + "USDT");
      if (index !== -1) {
        this.popupSymbolPrice = parseFloat(this.symbolPrice[index].price);
      }

    this.modalService.open(popupTemplate, { ariaLabelledBy: 'modal-basic-title', size: 'sm' }).result.then((result) => {
      if (result == 'sell') {
        this.sell(symbol, this.popupQty);
      }
      if (result == 'buy') {
        this.buy(symbol, this.popupQuoteQty);
      }
    }, (reason) => { });
  }

  onPopupQtyChange(){
    if(this.popupQty != 0) this.popupQuoteQty = this.popupSymbolPrice / this.popupQty;
  }
  onPopupQuoteQtyChange(){
    if(this.popupSymbolPrice != 0) this.popupQty = this.popupQuoteQty /this.popupSymbolPrice;
  }

  /////////////////////////////////////////////////////////////
  /////////////       API calls          //////////////////////
  /////////////////////////////////////////////////////////////
  async setServer(isProd): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Globals/SetServer/" + isProd,
    };
    return await this.httpService.xhr(httpSetting);
  }

  async getActiveServer(): Promise<boolean> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Globals/GetServer",
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

  async setTradeParam(): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Globals/SetTradeParameter/" + this.tradeOpen,
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

  async trade(): Promise<any> {
    //we allow system to execute orders
    this.tradeOpen = true;
    this.setTradeParam();

    //We start trading
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/AlgoTrade/MonitorMarket',
    };
    return await this.httpService.xhr(httpSetting);
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
      this.showMessageError = false
      this.mlUpdate();
    }, 2000);
  }

  async showChart(symbol, orderId) {
    if (orderId != null) {
      this.displaySymbol = symbol;
      this.myOrder = await this.orderDetailHelper.getOrder(orderId);
    }
    else {
      this.displaySymbol = symbol + "USDT";
      this.myOrder = null;
    }

    this.slope = await this.getSymbolMacdSlope(this.displaySymbol);
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

    this.chartOptions = {
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
            plotLines: [
              {
                color: '#5CE25C', width: 1, value: this.myOrder?.openPrice,
                label: { text: "Open            ", align: 'right' }
              },
              {
                color: '#FF8901', width: 1, value: this.myOrder?.closePrice,
                label: { text: "close", align: 'left' }
              },
            ],
          },
          {
            minorTickInterval: null,
            labels: { align: 'left', enabled: false },
            top: '60%',
            height: '40%',

            //offset: -5
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

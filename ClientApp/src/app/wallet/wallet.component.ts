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
  @ViewChild("lineChart", { static: false }) lineChart: any;
  highcharts = Highcharts;
  chartOptions: Highcharts.Options;

  model: NgbDateStruct;

  @ViewChild('p1') orderPopOver: any;
  @ViewChild("popupPrintScreen", { static: true }) popupPrintScreen: Element;


  private ohlc = [] as any;
  public pendingOrderList: Order[];
  public myAccount: BinanceAccount;
  public orderList: Order[];
  public logList: any[];
  public CandleList: any[];
  public totalProfit: number;
  public serverMsg: ServerMsg;
  public showMessageInfo: boolean = false;
  public showMessageError: boolean = false;
  public messageError: string;
  public interval: string;
  public intervalList = [] as any;
  public tradeOpen: boolean;
  public popupSymbol: string;
  public popupQty: number;
  public isCollapsed = true;

  color = 'accent';
  isProd = false;


  displaySymbol: string;
  displayOrder: Order;

  constructor(
    public modalService: NgbModal,
    private httpService: HttpService,
    private signalRService: SignalRService,
    private orderDetailHelper: OrderDetailHelper,
    private appSetting: AppSetting,
  ) {
    this.intervalList = this.appSetting.intervalList;
    this.interval = "1h";
  }

  async ngOnInit() {
    this.tradeOpen = false;
    this.setProdParam(this.isProd);
    this.model = this.today();
    this.orderList = await this.getAllOrder(this.model.day + "-" + this.model.month + "-" + this.model.year);
    this.calculateTotal();

    //Open listener on my API SignalR
    this.signalRService.startConnection();
    this.signalRService.openDataListener();

    this.signalRService.onMessage().subscribe(async message => {
      this.serverMsg = message;

      if (this.serverMsg.msgName == BackEndMessage.trading) {
        this.showMessageInfo = true;
        this.CandleList = this.serverMsg.candleList;

        this.orderList.forEach((order, index) => {
          this.CandleList.forEach(candle => {

            if (order.symbol === candle.s && !order.isClosed) {
              this.orderList[index].closePrice = candle.c;
              this.orderList[index].profit = (candle.c - order.openPrice) * order.quantity;
            }
          });
          this.calculateTotal();
        });
        setTimeout(() => { this.showMessageInfo = false }, 700);
      }

      if (this.serverMsg.msgName == BackEndMessage.newPendingOrder) {
        let binanceOrder = this.serverMsg.order;
        if (this.orderPopOver.isOpen()) {
          setTimeout(() => { this.openPopOver(this.orderPopOver, binanceOrder); }, 6000);
          setTimeout(() => { this.closePopOver(this.orderPopOver); }, 11000);
        }
        else {
          this.openPopOver(this.orderPopOver, binanceOrder);
          setTimeout(() => { this.closePopOver(this.orderPopOver); }, 5000);
        }
      }

      if (this.serverMsg.msgName == BackEndMessage.sellOrderFilled) {
        this.orderList = await this.getAllOrder(this.model.day + "-" + this.model.month + "-" + this.model.year);
        let binanceOrder = this.serverMsg.order;
        this.openPopOver(this.orderPopOver, binanceOrder);
        setTimeout(() => {
          this.closePopOver(this.orderPopOver);
          this.refreshUI();
        }, 5000);
      }

      if (this.serverMsg.msgName == BackEndMessage.newOrder) {
        this.orderList = await this.getAllOrder(this.model.day + "-" + this.model.month + "-" + this.model.year);
        this.calculateTotal();
        this.refreshUI();
      }

      if (this.serverMsg.msgName == BackEndMessage.exportChart) {
        this.showMessageError = true;
        this.messageError = "Exporting charts...";
         this.exportChart();
      }

      if (this.serverMsg.msgName == BackEndMessage.apiAccessFaulty
        || this.serverMsg.msgName == BackEndMessage.apiTooManyRequest
        || this.serverMsg.msgName == BackEndMessage.apiCheckAllowedIP
        || this.serverMsg.msgName == BackEndMessage.sellOrderExired) {
        this.showMessageError = true;
        this.messageError = this.serverMsg.msgName;
        setTimeout(() => { this.showMessageError = false }, 7000);
      }
    });

    //Get Wallet asset
    this.logList = await this.getLog();
    this.myAccount = await this.binanceAccount();
  }

  async refreshUI() {
    this.myAccount = await this.binanceAccount();
    this.logList = await this.getLog();
  }

  openPopOver(popover, order: BinanceOrder) {
    popover.open({ order });
  }

  closePopOver(popover) {
    if (popover.isOpen()) popover.close();
  }

  openPopup(popupTemplate, symbol, availableQty) {
    this.popupSymbol = symbol;
    this.popupQty = availableQty;
    this.modalService.open(popupTemplate, { ariaLabelledBy: 'modal-basic-title', size: 'sm' }).result.then((result) => {
      if (result == 'sell') {
        this.sell(symbol, this.popupQty);
      }
    }, (reason) => { });
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
    this.calculateTotal();
  }

  async changed() {
    await this.setProdParam(this.isProd);
    //this.assetList = await this.binanceAsset();
    this.myAccount = await this.binanceAccount();
  }

  calculateTotal() {
    this.totalProfit = 0;
    if (this.orderList.length > 0) {
      this.totalProfit = this.orderList.map(a => (a.profit)).reduce(function (a, b) {
        if (a != 0) return a + b;
      });
    }
  }

  async setProdParam(isProd): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Globals/SetProdParameter/" + isProd,
    };
    return await this.httpService.xhr(httpSetting);
  }

  async setTradeParam(isOpen): Promise<any> {
    this.tradeOpen = isOpen;
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Globals/SetTradeParameter/" + isOpen,
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

  async closeTrade(orderId): Promise<any> {
    console.log(orderId);
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/AutoTrade3/CloseTrade/' + orderId
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

  async testBuy(): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/AutoTrade3/TestBinanceBuy/',
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

  binanceAccount(): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/Binance/BinanceAccount',
    };
    return this.httpService.xhr(httpSetting);
  }

  async trade(): Promise<any> {
    //we allow system to execute orders
    this.tradeOpen = true;
    this.setTradeParam(true);

    //We start trading
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/AutoTrade3/MonitorMarket',
    };
    return await this.httpService.xhr(httpSetting);
  }

  async stopTrade(): Promise<any> {
    //we don't allow system to execute orders
    this.setTradeParam(false);
  }

  async resumTrade(): Promise<any> {
    //we don't allow system to execute orders
    this.setTradeParam(true);
  }

  getOpenDateTimeSpam(openDate) {
    if (openDate != null) {
      var openDateArr = openDate.split(" ")[0].split("/");
      var openTime = openDate.split(" ")[1] + " " + openDate.split(" ")[2];
      return Date.parse(openDateArr[2] + "/" + openDateArr[1] + "/" + openDateArr[0] + " " + openTime);
    }
  }



  /////////////////////////////////////////////////////////////
  /////////////    HighChart methods     //////////////////////
  ////////////////////////////////////////////////////////////?

  async exportChart() {
    this.interval = "1h";
    for (var i = 0; i < this.myAccount?.balances.length; i++) {
      if (this.myAccount?.balances[i].asset == "USDT") continue;
      await new Promise(next => {
        this.displaySymbol = this.myAccount?.balances[i].asset + "USDT";
        this.interval = "1h";
        this.displayHighstock();
        setTimeout(() => {
          this.lineChart.chart.exportChartLocal({
            type: "image/jpeg",
            filename: this.displaySymbol + new Date().getHours().toString(),
          });
          next("d");
        }, 3000);
      });
    }
    setTimeout(() => { this.showMessageError = false }, 2000);
  }

  async showChart(symbol, orderId) {
    if (orderId != null) {
      this.displaySymbol = symbol;
      this.displayOrder = await this.orderDetailHelper.getOrder(orderId);
    }
    else {
      this.displaySymbol = symbol + "USDT";
    }
    this.displayHighstock();
  }

  async changeHighstockResolution(key) {
    this.interval = key
    this.displayHighstock();
  }

  async displayHighstock() {
    this.ohlc = null;
    this.ohlc = await this.orderDetailHelper.getIntradayData(this.displaySymbol, 100, this.interval);

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

    this.chartOptions = {
      plotOptions: {
        candlestick: {
          upColor: '#41c9ad',
          color: '#cb585f',
          upLineColor: '#41c9ad',
          lineColor: '#cb585f'
        },
      },
      xAxis: {
        plotLines: [{
          color: '#5EFF00',
          width: 1,
          value: this.getOpenDateTimeSpam(this.displayOrder?.openDate),  //display openeing date
        }]
      },
      yAxis:
        [
          {
            crosshair: false,
            labels: { align: 'left' }, height: '60%', plotLines: [
              {
                color: '#5CE25C', width: 1, value: this.displayOrder?.openPrice,
                label: { text: "Open            ", align: 'right' }
              },
              {
                color: '#FF8901', width: 1, value: this.displayOrder?.closePrice,
                label: { text: "close", align: 'right' }
              },
            ],
          },
          {
            minorTickInterval: 0.1,
            labels: { align: 'left' }, top: '60%', height: '30%', offset: 0
          },
          {
            minorTickInterval: 0.1,
            labels: { align: 'left' }, top: '90%', height: '10%', offset: 0
          },
        ],
      chart: {
        type: "line",
        renderTo: "chart",
        events: {
          load: function (event) { }
        }
      },
    }

    this.chartOptions.series =
      [
        { data: chartData, type: 'candlestick', yAxis: 0, id: 'quote', name: 'quote' },
        { type: 'macd', yAxis: 1, linkedTo: 'quote', name: 'MACD' },
        { type: 'rsi', yAxis: 2, linkedTo: 'quote', name: 'RSI' }
      ]
  }
}

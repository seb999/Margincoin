import { Component } from '@angular/core';
import { NumericLiteral } from 'typescript';
import { Order } from '../class/order';
import { HttpSettings, HttpService } from '../service/http.service';
import { SignalRService } from '../service/signalR.service';
import { ServerMsg } from '../class/serverMsg';
import { Test } from '../class/Test';
import { NgbDateStruct } from '@ng-bootstrap/ng-bootstrap';
import { OrderDetailHelper } from './../orderDetail/orderDetail.helper';

import * as Highcharts from 'highcharts';
import HC_HIGHSTOCK from 'highcharts/modules/stock';
import HC_INDIC from 'highcharts/indicators/indicators';
import HC_RSI from 'highcharts/indicators/rsi';
import HC_EMA from 'highcharts/indicators/ema';
import HC_MACD from 'highcharts/indicators/macd';
import HC_THEME from 'highcharts/themes/dark-unica';
import { AppSetting } from '../app.settings';

HC_HIGHSTOCK(Highcharts);
HC_INDIC(Highcharts);
HC_RSI(Highcharts);
HC_EMA(Highcharts);
HC_MACD(Highcharts);
HC_THEME(Highcharts);

@Component({
  selector: 'app-wallet',
  templateUrl: './wallet.component.html',
})
export class WalletComponent {
  model: NgbDateStruct;

  private ohlc = [] as any;
  public PendingOrderList: Order[];
  public orderList: Order[];
  public assetList: any[];
  public CandleList: any[];
  public totalProfit: number;
  public serverMsg: ServerMsg;
  public showMessageInfo: boolean = false;
  public showMessageError: boolean = false;
  public messageError: string;
  public interval: string;
  public intervalList = [] as any;

  color = 'accent';
  isProd = false;

  highcharts = Highcharts;
  chartOptions: Highcharts.Options;
  displaySymbol: string;
  displayOrder: Order;

  constructor(
    private httpService: HttpService,
    private signalRService: SignalRService,
    private orderDetailHelper: OrderDetailHelper,
    private appSetting: AppSetting,
  ) {
    this.intervalList = this.appSetting.intervalList;
    this.interval = "1h";
  }

  async ngOnInit() {
    this.setProdParam(this.isProd);
    this.model = this.today();
    this.orderList = await this.getAllOrder(this.model.day + "-" + this.model.month + "-" + this.model.year);
    this.calculateTotal();

    //Open listener on my API SignalR
    this.signalRService.startConnection();
    this.signalRService.openDataListener();

    this.signalRService.onMessage().subscribe(async message => {
      this.serverMsg = message;

      if (this.serverMsg.msgName == 'trading') {
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

      if (this.serverMsg.msgName == 'newOrder') {
        this.orderList = await this.getAllOrder(this.model.day + "-" + this.model.month + "-" + this.model.year);
        this.calculateTotal();
      }

      if (this.serverMsg.msgName == 'binanceAccessFaulty' || this.serverMsg.msgName == 'binanceTooManyRequest' || this.serverMsg.msgName == 'binanceCheckAllowedIP') {
        this.showMessageError = true;
        this.messageError = this.serverMsg.msgName;
        setTimeout(() => { this.showMessageError = false }, 10000);
      }
    });

    //Get Wallet asset
    this.assetList = await this.binanceAsset();
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
    console.log(this.isProd);
    await this.setProdParam(this.isProd);
    this.assetList = await this.binanceAsset();
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

  async getSymbolWeight(): Promise<Order[]> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/AutoTrade2/GetSymbolWeight",
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

  async monitorMarket(): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/AutoTrade2/MonitorMarket',
    };
    return await this.httpService.xhr(httpSetting);
  }

  async closeTrade(orderId, lastPrice): Promise<any> {
    console.log(lastPrice);
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/AutoTrade3/CloseTrade/' + orderId + '/' + lastPrice,
    };
    return await this.httpService.xhr(httpSetting);
  }

  async testBuy(): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: 'https://localhost:5002/api/AutoTrade3/TestBinanceBuy/',
    };
    return await this.httpService.xhr(httpSetting);
  }

  async binanceAsset(): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: 'https://localhost:5002/api/AutoTrade3/BinanceAsset',
    };
    return await this.httpService.xhr(httpSetting);
  }

  async monitorMyList(): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + '/api/AutoTrade3/MonitorMarket',
    };
    return await this.httpService.xhr(httpSetting);
  }

  getOpenDateTimeSpam(openDate) {
    var openDateArr = openDate.split(" ")[0].split("/");
    var openTime = openDate.split(" ")[1] + " " + openDate.split(" ")[2];
    return Date.parse(openDateArr[2] + "/" + openDateArr[1] + "/" + openDateArr[0] + " " + openTime);
  }

  async showChart(symbol, orderId) {
    this.displaySymbol = symbol;
    this.displayOrder = await this.orderDetailHelper.getOrder(orderId);
    this.displayHighstock();
  }

  async changeHighstockResolution(key) {
    let params = key.split(',');
    this.interval = params[0];
    this.displayHighstock();
  }

  async displayHighstock() {

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
          width: 2,
          //value: this.getOpenDateTimeSpam(this.openOrder?.openDate),  //display openeing date
        }]
      },
      yAxis:
        [
          {
            crosshair: true,
            labels: { align: 'left' }, height: '80%', plotLines: [
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
          { labels: { align: 'left' }, top: '80%', height: '20%', offset: 0 },
        ],
    }

    this.chartOptions.series =
      [
        { data: chartData, type: 'candlestick', yAxis: 0, id: 'quote', name: 'quote' },
        { type: 'macd', yAxis: 1, linkedTo: 'quote', name: 'MACD' }
      ]

  }
}

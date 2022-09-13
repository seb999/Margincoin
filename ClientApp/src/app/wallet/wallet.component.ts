import { Component } from '@angular/core';
import { NumericLiteral } from 'typescript';
import { Order } from '../class/order';
import { HttpSettings, HttpService } from '../service/http.service';
import { SignalRService } from '../service/signalR.service';
import { ServerMsg } from '../class/serverMsg';
import {NgbDateStruct} from '@ng-bootstrap/ng-bootstrap';

import * as Highcharts from 'highcharts';
import HC_HIGHSTOCK from 'highcharts/modules/stock';
import HC_INDIC from 'highcharts/indicators/indicators';
import HC_RSI from 'highcharts/indicators/rsi';
import HC_EMA from 'highcharts/indicators/ema';
import HC_MACD from 'highcharts/indicators/macd';
import HC_THEME from 'highcharts/themes/grid-light';

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

  public orderList: Order[];
  public orderFilter: Order[];
  public SymbolWeightList: any[];
  public totalProfit: number;
  public serverMsg: ServerMsg;
  public showMessageInfo: boolean = false;
  color = 'accent';
  checked = false;

  highcharts = Highcharts;
  chartOptions: Highcharts.Options;

  constructor(
    private httpService: HttpService,
    private signalRService: SignalRService,
  ) {
  }

  async ngOnInit() {
    //Display list of open orders
    this.orderList = await this.getOpenOrder();
    this.filterOrderList();
    this.calculateTotal();

    //Open listener on my API SignalR
    this.signalRService.startConnection();
    this.signalRService.addTransferChartDataListener();

    //Display last closed/current trade chart
    this.displayHighstock('MACD');

    this.signalRService.onMessage().subscribe(async message => {
      this.serverMsg = message;
     
      if (this.serverMsg.msgName == 'trading') {
        this.showMessageInfo = true;
        this.SymbolWeightList = this.serverMsg.symbolWeight;
        setTimeout(() => { this.showMessageInfo = false }, 700);
      }

      if (this.serverMsg.msgName == 'newOrder') {
        this.orderList = await this.getOpenOrder();
        this.orderFilter = this.orderList;
        this.calculateTotal();
      }
    });
  }

  today() {
    var d = new Date();
    return {day : d.getDate(), month: d.getMonth()+1, year : d.getFullYear()};
  }

  filterOrderList() {
    console.log(this.model);
    if(this.model==undefined){
      this.orderFilter = this.orderList;
      return;
    } 
    this.orderFilter = this.orderList.filter((p: any) => {
     var myDate = p.openDate.split("/");  
     return new Date(myDate[1] + "/" + myDate[0] + "/" + myDate[2]).getTime() > new Date(this.model.month + "/" + this.model.day + "/" + this.model.year).getTime(); 
  });

  this.calculateTotal();
  }

  changed(){
    console.log(this.checked)
  }

  async displayHighstock(indicator) {

    // this.openOrder = await this.orderDetailHelper.getOrder(this.orderLis);

    // let chartData = [] as any;
    // let volume = [] as any;

    // this.ohlc.map((data, index) => {
    //   chartData.push([
    //     parseFloat(data[0]),
    //     parseFloat(data[1]),
    //     parseFloat(data[2]),
    //     parseFloat(data[3]),
    //     parseFloat(data[4]),
    //   ]),
    //     volume.push([
    //       parseFloat(data[0]),
    //       parseFloat(data[5]),
    //     ])
    // });

    // this.chartOptions = {
    //   plotOptions: {
    //     candlestick: {
    //       upColor: '#41c9ad',
    //       color: '#cb585f',
    //       upLineColor: '#41c9ad',
    //       lineColor: '#cb585f'
    //     },
    //   },
    //   xAxis: {
    //     plotLines: [{
    //         color: '#5EFF00', 
    //         width: 2,
    //         value: this.getOpenDateTimeSpam(this.openOrder?.openDate),  //display openeing date
    //     }]
    // },
    //   yAxis:
    //     [
    //       {
    //         crosshair : true,
    //         labels: { align: 'left' }, height: '80%', plotLines: [
    //           {
    //             color: '#FF8901', width: 1, value: this.openOrder?.openPrice,
    //             label: { text: "Open            ", align: 'right' }
    //           },
    //           // {
    //           //   color: '#ff3339', width: 1, value: this.openOrder?.stopLose,
    //           //   label: { text: "stopLose", align: 'right' }
    //           // },
    //           // {
    //           //   color: '#ff9333', width: 1, value: (this.openOrder?.highPrice * (1 - (this.openOrder?.takeProfit / 100))),
    //           //   label: { text: "take profit", align: 'right' }
    //           // },
    //           {
    //             color: '#333eff', width: 1, value: this.openOrder?.closePrice,
    //             label: { text: "close", align: 'right' }
    //           },
    //         ],
    //       },
    //       { labels: { align: 'left' }, top: '80%', height: '20%', offset: 0 },
    //     ],
    // }

    // if (indicator == 'NO_INDICATOR') {
    //   this.chartOptions.series =
    //     [
    //       { data: chartData, type: 'candlestick', yAxis: 0, name: 'quote' },
    //       { data: volume, type: 'line', yAxis: 1, name: 'volume' }
    //     ];
    // }

    // if (indicator == 'MACD') {
    //   this.chartOptions.series =
    //     [
    //       { data: chartData, type: 'candlestick', yAxis: 0, id: 'quote', name: 'quote' },
    //       { type: 'macd', yAxis: 1, linkedTo: 'quote', name: 'MACD' }
    //     ]
    // }
  }


  calculateTotal(){
    this.totalProfit = 0;
    if (this.orderFilter.length > 0) {
      this.totalProfit = this.orderFilter.map(a => (a.profit)).reduce(function (a, b) {
        if(a!=0) return a + b;
      });
    }
  }

  async getSymbolWeight(): Promise<Order[]> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/AutoTrade2/GetSymbolWeight",
    };
    return await this.httpService.xhr(httpSetting);
  }

  async getOpenOrder(): Promise<Order[]> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Order/GetAllCompletedOrder/",
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

  async testBuy(): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: 'https://localhost:5002/api/AutoTrade2/TestBinanceBuy',
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

  getOpenDateTimeSpam(openDate){
    var openDateArr = openDate.split(" ")[0].split("/");
    var openTime = openDate.split(" ")[1] + " " + openDate.split(" ")[2];
    return Date.parse(openDateArr[2] + "/" + openDateArr[1] + "/" + openDateArr[0] + " " + openTime);
  }
}

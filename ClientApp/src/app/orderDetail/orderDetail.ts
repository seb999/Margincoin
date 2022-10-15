import { OrderDetailHelper } from './orderDetail.helper';
import { Component, ViewEncapsulation } from '@angular/core';
import { ActivatedRoute } from "@angular/router";
import { WebSocket1Service } from '../service/websocket1.service';
import { WebSocket2Service } from '../service/websocket2.service';
import { NgbModal } from '@ng-bootstrap/ng-bootstrap';
import { map, takeUntil } from 'rxjs/operators';
import { environment } from 'src/environments/environment';
import { Observable, Subject } from 'rxjs';
import { HttpSettings, HttpService } from '../service/http.service';
import { Order } from '../class/order';
import { SignalRService } from '../service/signalR.service';

export const WS_SYMBOL_ENDPOINT = environment.wsEndPointSymbol;
export const SOUND = environment.soundUp;

import * as Highcharts from 'highcharts';
import HC_HIGHSTOCK from 'highcharts/modules/stock';
import HC_INDIC from 'highcharts/indicators/indicators';
import HC_RSI from 'highcharts/indicators/rsi';
import HC_EMA from 'highcharts/indicators/ema';
import HC_MACD from 'highcharts/indicators/macd';
import HC_THEME from 'highcharts/themes/dark-unica';
import { AppSetting } from '../app.settings';
import { OrderTemplate } from '../class/orderTemplate';
import { ServerMsg } from '../class/serverMsg';
import { Depth } from '../class/depth';

HC_HIGHSTOCK(Highcharts);
HC_INDIC(Highcharts);
HC_RSI(Highcharts);
HC_EMA(Highcharts);
HC_MACD(Highcharts);
HC_THEME(Highcharts);

@Component({
  selector: 'app-orderDetail',
  templateUrl: './orderDetail.component.html',
  encapsulation: ViewEncapsulation.None,
  providers: [WebSocket1Service, WebSocket2Service]
})
export class OrderDetailComponent {
  public tickerDataListener$: Observable<any>;
  public depthDataListener$: Observable<any>;
  private unsubscribe$: Subject<void>;
  public orderId: string;
  public coinData: any;
  public takeProfit: number;
  public takeProfitOffset: number;

  public orderModel: Order;
  public orderTemplate: OrderTemplate;
  public serverMsg: ServerMsg;
  public showMessageInfo: boolean = false;
  public intervalList = [] as any;
  public interval: string;
  public askRatio: number = 0;
  public bidRatio: number = 0;

  private depthList: Depth[] = new Array();
  private ohlc = [] as any;

  highcharts = Highcharts;
  chartOptions: Highcharts.Options;
  chartOptions2: Highcharts.Options;
  openOrder: Order;

  constructor(
    private httpService: HttpService,
    private route: ActivatedRoute,
    private service$: WebSocket1Service,
    private service2$: WebSocket2Service,
    public modalService: NgbModal,
    private orderDetailHelper: OrderDetailHelper,
    private appSetting: AppSetting,
    private signalRService: SignalRService,
  ) {
    this.unsubscribe$ = new Subject<void>();
    this.coinData = {};
    this.takeProfitOffset = 0.008;
    this.intervalList = this.appSetting.intervalList;
    this.interval = "30m";
  }

  async ngOnInit() {
    this.orderId = this.route.snapshot.paramMap.get("id");
    this.openOrder = await this.orderDetailHelper.getOrder(this.orderId);
    this.ohlc = await this.orderDetailHelper.getIntradayData(this.openOrder.symbol, 100, this.interval);

    //Display historic chart
    this.displayHighstock('MACD');

    //Open listener on my API SignalR
    this.signalRService.startConnection();
    this.signalRService.addTransferChartDataListener();
    this.signalRService.onMessage().subscribe(message => {
      this.serverMsg = message;
      this.showMessageInfo = true;
      if (this.serverMsg.msgName == 'refreshUI') {
        this.loadOrder();
      }
      setTimeout(() => { this.showMessageInfo = false }, 700);
    });

    //Stream ticker
    this.tickerDataListener$ = this.service$.connect(WS_SYMBOL_ENDPOINT + this.openOrder.symbol.toLowerCase() + "@kline_5m").pipe(
      map(
        (response: MessageEvent): any => {
          let data = JSON.parse(response.data);
          return data;
        }
      )
    );

    //Stream depth
    this.depthDataListener$ = this.service2$.connect(WS_SYMBOL_ENDPOINT + this.openOrder.symbol.toLowerCase() + "@depth").pipe(
      map(
        (response: MessageEvent): any => {
          let data = JSON.parse(response.data);
          return data;
        }
      )
    );

    this.depthDataListener$
      .pipe(takeUntil(this.unsubscribe$))
      .subscribe(data => {
        let ask: number = 0;
        let bid: number = 0;

        //bid (price*quantity)
        data.b.map(p => {
          bid = bid + p[0] * p[1];
        })

        //ask (price*quantity)
        data.a.map(p => {
          ask = ask + p[0] * p[1];
        })

        if (this.depthList.length > 10) {
          this.depthList.push(new Depth(ask, bid));
          this.depthList.splice(0, 1);
        }
        else {
          this.depthList.push(new Depth(ask, bid))
        }
        ask = 0;
        bid = 0;
        this.depthList.map(p => {
          ask = ask + p.ask;
          bid = bid + p.bid
        })
        this.bidRatio = (bid / (ask + bid)) * 100;
        this.askRatio = (ask / (ask + bid)) * 100;
      });

    this.tickerDataListener$
      .pipe(takeUntil(this.unsubscribe$))
      .subscribe(data => {
        this.coinData = data;
        if (this.coinData.p < 0) this.coinData.color = "red";
        if (this.coinData.p >= 0) this.coinData.color = "limegreen";
        this.coinData.s.includes("USDT");

        if (!this.coinData.k.x) {
          this.ohlc.pop();
        }
        this.ohlc.push([
          this.coinData.E, this.coinData.k.o, this.coinData.k.h, this.coinData.k.l, this.coinData.k.c
        ])
        

        this.displayHighstock('MACD');
      });

  }

  async loadOrder() {
    this.openOrder = await this.orderDetailHelper.getOrder(this.orderId);
  }

  changeTakeProfitOffset(type: string) {
    if (type == 'increase') { this.takeProfitOffset = this.takeProfitOffset + 0.001 };
    if (type == 'reduce') { this.takeProfitOffset = this.takeProfitOffset - 0.001 }
  }

  processFormInputChange(formImputChanged) {
    switch (formImputChanged) {
      case 'amount':
        this.orderModel.quantity = this.orderModel.amount / this.orderModel.openPrice;
        break;
      case 'quantity':
        this.orderModel.amount = this.orderModel.quantity * this.orderModel.openPrice;
      default:
        this.orderModel.quantity = this.orderModel.amount / this.orderModel.openPrice;
        this.orderModel.amount = this.orderModel.quantity * this.orderModel.openPrice;
        break;
    }
  }

  async openPopupOrderTemplate(template) {
    this.orderTemplate = await this.getOrderTemplate();
    if (this.orderTemplate == null) {
      this.orderTemplate = new OrderTemplate(0, this.openOrder.symbol, 0, 1, 1);
    }
    this.modalService.open(template, { ariaLabelledBy: 'modal-basic-title', size: 'sm' }).result.then((result) => {
      if (result == 'continue') {
        this.saveOrderTemplate();
      }
    }, (reason) => { });
  }

  async getOrderTemplate(): Promise<OrderTemplate> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: 'https://localhost:5002/api/AutoTrade/GetOrderTemplate/',
    };
    return await this.httpService.xhr(httpSetting);
  }

  async saveOrderTemplate() {
    const httpSetting: HttpSettings = {
      method: 'POST',
      data: this.orderTemplate,
      url: 'https://localhost:5002/api/AutoTrade/SaveOrderTemplate/',
    };
    return await this.httpService.xhr(httpSetting);
  }

  async closeOrder(orderId: number, closePrice: number) {
    await this.orderDetailHelper.closeOrder(orderId, closePrice);
    this.openOrder = await this.orderDetailHelper.getOrder(this.orderId);
  }

  async displayHighstock(indicator) {

    this.openOrder = await this.orderDetailHelper.getOrder(this.orderId);

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
            value: this.getOpenDateTimeSpam(this.openOrder?.openDate),  //display openeing date
        }]
    },
      yAxis:
        [
          {
            crosshair : true,
            labels: { align: 'left' }, height: '80%', plotLines: [
              {
                color: '#FF8901', width: 1, value: this.openOrder?.openPrice,
                label: { text: "Open            ", align: 'right' }
              },
              // {
              //   color: '#ff3339', width: 1, value: this.openOrder?.stopLose,
              //   label: { text: "stopLose", align: 'right' }
              // },
              // {
              //   color: '#ff9333', width: 1, value: (this.openOrder?.highPrice * (1 - (this.openOrder?.takeProfit / 100))),
              //   label: { text: "take profit", align: 'right' }
              // },
              {
                color: '#333eff', width: 1, value: this.openOrder?.closePrice,
                label: { text: "close", align: 'right' }
              },
            ],
          },
          { labels: { align: 'left' }, top: '80%', height: '20%', offset: 0 },
        ],
    }

    if (indicator == 'NO_INDICATOR') {
      this.chartOptions.series =
        [
          { data: chartData, type: 'candlestick', yAxis: 0, name: 'quote' },
          { data: volume, type: 'line', yAxis: 1, name: 'volume' }
        ];
    }

    if (indicator == 'MACD') {
      this.chartOptions.series =
        [
          { data: chartData, type: 'candlestick', yAxis: 0, id: 'quote', name: 'quote' },
          { type: 'macd', yAxis: 1, linkedTo: 'quote', name: 'MACD' }
        ]
    }
  }

  async changeHighstockResolution(key) {
    let params = key.split(',');
    this.ohlc = await this.orderDetailHelper.getIntradayData(this.openOrder.symbol, 100, params[0]);
    this.displayHighstock('MACD');
  }

  getOpenDateTimeSpam(openDate){
    var openDateArr = openDate.split(" ")[0].split("/");
    var openTime = openDate.split(" ")[1] + " " + openDate.split(" ")[2];
    return Date.parse(openDateArr[2] + "/" + openDateArr[1] + "/" + openDateArr[0] + " " + openTime);
  }

  ngOnDestroy() {
    //Unsubscribe to Binance stream
    this.service$.close();
    this.unsubscribe$.next();
    this.unsubscribe$.complete();

    //Unsubscribe to my API stream (SignalR)
    this.signalRService.closeConnection();
  }
}
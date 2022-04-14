import { Component, ViewEncapsulation, ɵCodegenComponentFactoryResolver } from '@angular/core';
import { ActivatedRoute } from "@angular/router";
import { WebSocket1Service } from '../service/websocket1.service';
import { WebSocket2Service } from '../service/websocket2.service';
import { NgbModal, ModalDismissReasons } from '@ng-bootstrap/ng-bootstrap';
import { map, takeUntil } from 'rxjs/operators';
import { environment } from 'src/environments/environment';
import { Observable, Subject } from 'rxjs';
import { HttpSettings, HttpService } from '../service/http.service';
import { TradingboardHelper } from '../tradingboard/tradingBoard.helper';
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
import HC_THEME from 'highcharts/themes/grid-light';
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
  selector: 'app-tradingboard',
  templateUrl: './tradingboard.component.html',
  encapsulation: ViewEncapsulation.None,
  providers: [WebSocket1Service, WebSocket2Service]
})
export class TradingboardComponent {
  public symbolList$: Observable<any>;
  public tickerDataListener$: Observable<any>;
  public depthDataListener$: Observable<any>;
  private unsubscribe$: Subject<void>;
  public symbol: string;
  public coinData: any;
  public stop: number;
  public takeProfit: number;
  public takeProfitOffset: number;
  
  public popupTitle: string;
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
  public liveChartData = [] as any;
  public liveChartVolum = [] as any;
  public liveDataAI = [] as any;

  highcharts = Highcharts;
  highcharts2 = Highcharts;
  highcharts3 = Highcharts;
  chartOptions: Highcharts.Options;
  chartOptions2: Highcharts.Options;
  openOrderList: Order[];

  constructor(
    private httpService: HttpService,
    private route: ActivatedRoute,
    private service$: WebSocket1Service,
    private service2$: WebSocket2Service,
    public modalService: NgbModal,
    private tradingboardHelper: TradingboardHelper,
    private appSetting: AppSetting,
    private signalRService: SignalRService,
  ) {
    this.unsubscribe$ = new Subject<void>();
    this.coinData = {};
    this.takeProfitOffset = 0.008;
    this.intervalList = this.appSetting.intervalList;
    this.interval = "15m";
  }

  async ngOnInit() {
    this.symbol = this.route.snapshot.paramMap.get("symbol");

    //Display historic chart
    this.displayHighstock('NO_INDICATOR', '15m');

    //Display list of open orders
    this.loadOrderList();

    //Open listener on my API SignalR
    this.signalRService.startConnection();
    this.signalRService.addTransferChartDataListener();
    this.signalRService.onMessage().subscribe(message => {
      this.serverMsg = message;
      this.showMessageInfo = true;
      if (this.serverMsg.msgName == 'refreshUI') {
        let snd = new Audio(SOUND);
        snd.play();
        this.loadOrderList();
      }
      setTimeout(() => { this.showMessageInfo = false }, 700);
    });

    //Stream ticker
    this.tickerDataListener$ = this.service$.connect(WS_SYMBOL_ENDPOINT + this.symbol.toLowerCase() + "@ticker").pipe(
      map(
        (response: MessageEvent): any => {
          let data = JSON.parse(response.data);
          return data;
        }
      )
    );

    //Stream depth
    this.depthDataListener$ = this.service2$.connect(WS_SYMBOL_ENDPOINT + this.symbol.toLowerCase() + "@depth").pipe(
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

        //re calculate stop loose
        this.takeProfit = this.coinData.c - ((this.coinData.c / 100) * this.takeProfitOffset);
        this.displayHighchart(this.coinData);

        //Calculate Prediction
        this.getPrediction(this.coinData).then((p) => {
         //this.predictionList = p;
         console.log(p);
        });
      });

  }

  async loadOrderList() {
    this.openOrderList = await this.tradingboardHelper.getOpenOrder(this.symbol);
  }

  changetakeProfitOffset(type: string) {
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

  async openPopUpOrder(template) {
    this.popupTitle = "Buy " + this.symbol;
    this.orderModel = new Order(0, this.symbol, 0, 0, this.coinData.c, 1, 1);
    this.modalService.open(template, { ariaLabelledBy: 'modal-basic-title', size: 'sm' }).result.then((result) => {
      if (result == 'continue') {
        this.openOrder()
      }
    }, (reason) => { });
  }

  async openPopupOrderTemplate(template) {
    this.orderTemplate = await this.getOrderTemplate();
    if (this.orderTemplate == null) {
      this.orderTemplate = new OrderTemplate(0, this.symbol, 0, 1, 1);
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

  async startTrade() {
    {
      const httpSetting: HttpSettings = {
        method: 'GET',
        url: location.origin + "/api/AutoTrade/StartTrade/" + this.symbol,
      };
      return await this.httpService.xhr(httpSetting);
    }
  }

  async stopTrade() {
    {
      const httpSetting: HttpSettings = {
        method: 'GET',
        url: location.origin + "/api/AutoTrade/StopTrade/",
      };
      return await this.httpService.xhr(httpSetting);
    }
  }

  async openOrder() {
    await this.tradingboardHelper.openOrder(this.orderModel);
    this.openOrderList = await this.tradingboardHelper.getOpenOrder(this.symbol);
  }

  async closeOrder(orderId: number, closePrice: number) {
    await this.tradingboardHelper.closeOrder(orderId, closePrice);
    this.openOrderList = await this.tradingboardHelper.getOpenOrder(this.symbol);
  }

  async displayHighchart(coinData: any) {
    this.liveChartData.push([
      parseFloat(coinData.E),
      parseFloat(coinData.c),
    ]);

    this.liveChartVolum.push([
      parseFloat(coinData.E),
      parseFloat(coinData.v),
    ]);

    this.chartOptions2 = {
      series: [
        { data: this.liveChartData, type: 'line', yAxis: 0, name: 'last' },
        { data: this.liveChartVolum, type: 'line', yAxis: 1, name: 'volume' }
      ],
      title: {
        text: ''
      },
      xAxis: { type: 'datetime', dateTimeLabelFormats: { minute: '%M', }, title: { text: '' } },
      yAxis:
        [{
          labels: { align: 'left' }, height: '80%',
          plotLines: [
            {
              color: '#32CD32', width: 1, value: this.takeProfit,
              label: { text: this.takeProfitOffset.toFixed(4).toString(), align: 'right' }
            },
            {
              color: '#FF0000', width: 2, value: this.openOrderList[0]?.openPrice * (1 - (this.openOrderList[0]?.stopLose / 100)),
              label: { text: this.openOrderList[0]?.stopLose.toString(), align: 'right' }
            },
            {
              color: '#4169E1', width: 2, value: this.openOrderList[0]?.openPrice,
              label: { text: this.openOrderList[0]?.openPrice.toString(), align: 'right' }
            },
            {
              color: '#cc4e20f3', width: 2, value: this.serverMsg?.r1,
              label: { text: 'R1', align: 'right' }
            },
            {
              color: '#cc4e20f3', width: 2, value: this.serverMsg?.s1,
              label: { text: 'S1', align: 'right' }
            },
          ],
        },
        { labels: { align: 'left' }, top: '80%', height: '20%', offset: 0 },
        ],
    }
  }

  async displayHighstock(indicator, interval: string) {
    let chartData = [] as any;
    let volume = [] as any;
    this.ohlc = await this.tradingboardHelper.getIntradayData(this.symbol, 1000, interval);

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
      yAxis:
        [
          { labels: { align: 'left' }, height: '80%' },
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

    if (indicator == 'RSI') {
      this.chartOptions.series =
        [
          { data: chartData, type: 'candlestick', yAxis: 0, id: 'oil' },
          { type: 'rsi', yAxis: 1, linkedTo: 'oil', name: 'RSI' },
          { data: volume, type: 'line', yAxis: 2 }
        ]
    }

    if (indicator == 'MACD') {
      this.chartOptions.series =
        [
          { data: chartData, type: 'candlestick', yAxis: 0, id: 'oil' },
          { type: 'macd', yAxis: 1, linkedTo: 'oil', name: 'MACD' }
        ]
    }
  }

  changeHighstockResolution(key) {
    let params = key.split(',');
    console.log(key);
    this.displayHighstock('NO_INDICATOR', key);
  }

  async getPrediction(coinData): Promise<any> {
    this.liveDataAI.push({
      s: coinData.s,
      c: coinData.c,
      v: coinData.v,
    })

    const httpSetting: HttpSettings = {
      method: 'POST',
      data: this.liveDataAI,
      url: location.origin + "/api/AI/GetPrediction/",
    };
    return await this.httpService.xhr(httpSetting);
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
import { Component } from '@angular/core';
import { ActivatedRoute } from "@angular/router";
import { WebSocketService } from '../service/websocket.service';
import { NgbModal, ModalDismissReasons } from '@ng-bootstrap/ng-bootstrap';
import { map, takeUntil } from 'rxjs/operators';
import { environment } from 'src/environments/environment';
import { Observable, Subject } from 'rxjs';
import { HttpSettings, HttpService } from '../service/http.service';
import { TradingboardHelper } from '../tradingboard/tradingBoard.helper';
import { Order } from '../class/order';

export const WS_SYMBOL_ENDPOINT = environment.wsEndPointSymbol;

import * as Highcharts from 'highcharts';
import HC_HIGHSTOCK from 'highcharts/modules/stock';
import HC_INDIC from 'highcharts/indicators/indicators';
import HC_RSI from 'highcharts/indicators/rsi';
import HC_EMA from 'highcharts/indicators/ema';
import HC_MACD from 'highcharts/indicators/macd';
import HC_THEME from 'highcharts/themes/grid-light';
import { AppSetting } from '../app.settings';

HC_HIGHSTOCK(Highcharts);
HC_INDIC(Highcharts);
HC_RSI(Highcharts);
HC_EMA(Highcharts);
HC_MACD(Highcharts);
HC_THEME(Highcharts);

@Component({
  selector: 'app-tradingboard',
  templateUrl: './tradingboard.component.html',
})
export class TradingboardComponent {
  public symbolList$: Observable<any>;
  public symbolDataListener$: Observable<any>;
  private unsubscribe$: Subject<void>;
  public symbol: string;
  public coinData: any;
  public stop: number;
  public stopLose: number;
  public stopLoseOffset: number;
  private ohlc = [] as any;
  public popupTitle: string;
  public orderModel: Order;

  public intervalList = [] as any;
  public interval: string;

  public liveChartData = [] as any;
  public liveChartVolum = [] as any;
  highcharts = Highcharts;
  highcharts2 = Highcharts;
  highcharts3 = Highcharts;
  chartOptions: Highcharts.Options;
  chartOptions2: Highcharts.Options;
  openOrderList: any;

  constructor(
    private httpService: HttpService,
    private route: ActivatedRoute,
    private service$: WebSocketService,
    public modalService: NgbModal,
    private tradingboardHelper: TradingboardHelper,
    private appSetting: AppSetting,
  ) {
    this.unsubscribe$ = new Subject<void>();
    this.coinData = {};
    this.stopLoseOffset = 0.008;
    this.intervalList = this.appSetting.intervalList;
    this.interval = "4h";
  }

  async ngOnInit() {
    this.symbol = this.route.snapshot.paramMap.get("symbol");

    //Display historic chart
    this.displayHighstock('NO_INDICATOR', '4h');

    //Display list of open orders
    this.openOrderList = await this.tradingboardHelper.getOpenOrder(this.symbol);

    //Open listener
    this.symbolDataListener$ = this.service$.connect(WS_SYMBOL_ENDPOINT + this.symbol.toLowerCase() + "@ticker").pipe(
      map(
        (response: MessageEvent): any => {
          let data = JSON.parse(response.data);
          return data;
        }
      )
    );

    this.symbolDataListener$
      .pipe(takeUntil(this.unsubscribe$))
      .subscribe(data => {
        this.coinData = data;
        if (this.coinData.p < 0) this.coinData.color = "red";
        if (this.coinData.p >= 0) this.coinData.color = "limegreen";
        this.coinData.s.includes("USDT");

        //auto sell
         this.bigBrother(this.coinData.c);

        this.displayHighchart(this.coinData);
      });
  }

  async bigBrother(price: number) {
    if (price <= this.stopLose) (console.log("sold"));
    
    this.openOrderList.map(order=>{
      if (price <= order.orderStopLose) (console.log("sold AND TAKE YOUR LOOSE"))
    })
   
    //re calculate stop loose
    this.stopLose = price - ((price/100) * this.stopLoseOffset);
  }

  changeStopLoseOffset(type: string) {
    if (type == 'increase') { this.stopLoseOffset = this.stopLoseOffset + 0.001 };
    if (type == 'reduce') { this.stopLoseOffset = this.stopLoseOffset - 0.001 }
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

  openPopup(template, action) {
   
    switch (action) {
      case "newOrder":
        this.popupTitle = "Buy " + this.symbol;
        this.orderModel = new Order(0, this.symbol, 0, 0, this.coinData.c, 1, 1);
        this.modalService.open(template, { ariaLabelledBy: 'modal-basic-title', size: 'sm' }).result.then((result) => {
          if (result == 'openOrder') {
            this.openOrder()
          }
        }, (reason) => { });
        break;

      default:
        break;
    }
  }

  async openOrder(){
    await this.tradingboardHelper.openOrder(this.orderModel);
    this.openOrderList = await this.tradingboardHelper.getOpenOrder(this.symbol);
  }

  async closeOrder(orderId : number, closePrice : number){
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
        text: this.stopLose.toString()
      },
      xAxis: { type: 'datetime', dateTimeLabelFormats: { minute: '%M', }, title: { text: '' } },
      yAxis:
        [{
          labels: { align: 'left' }, height: '80%',
          plotLines: [
            {
              color: '#32CD32', width: 1, value: this.stopLose,
              label: { text: this.stopLoseOffset.toFixed(4).toString(), align: 'right' }
            },
            {
              //for many order, we need one line by order
              color: '#FF0000', width: 2, value: this.openOrderList[0]?.stopLose,
              label: { text: this.openOrderList[0]?.stopLose, align: 'right' }
            }
          ],
        },
        { labels: { align: 'left' }, top: '80%', height: '20%', offset: 0 },
        ],
    }
  }

  async displayHighstock(indicator, interval: string) {
    let chartData = [] as any;
    let volume = [] as any;
    this.ohlc = await this.tradingboardHelper.getIntradayData(this.symbol, interval);

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
    this.displayHighstock('NO_INDICATOR', key);
  }

  ngOnDestroy() {
    this.service$.close();
    this.unsubscribe$.next();
    this.unsubscribe$.complete();
  }
}
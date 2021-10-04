import { Component } from '@angular/core';
import { ActivatedRoute } from "@angular/router";
import { WebSocketService } from '../service/websocket.service';
import * as Highcharts from 'highcharts';
import { map, takeUntil } from 'rxjs/operators';
import { environment } from 'src/environments/environment';
import { Observable, Subject } from 'rxjs';
import { HttpSettings, HttpService } from '../service/http.service';

export const WS_SYMBOL_ENDPOINT = environment.wsEndPointSymbol;

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

  liveChartData = [] as any;
  highcharts = Highcharts;
  highcharts2 = Highcharts;
  highcharts3 = Highcharts;
  chartOptions: Highcharts.Options;
  chartOptions2: Highcharts.Options;
  chartOptions3: Highcharts.Options;
  openOrderList: any;

  constructor(
    private httpService: HttpService,
    private route: ActivatedRoute,
    private service$: WebSocketService
  ) {
    this.unsubscribe$ = new Subject<void>();
    this.coinData = {};
    this.stopLoseOffset = 0.0008;
  }

  async ngOnInit() {
    this.symbol = this.route.snapshot.paramMap.get("symbol");
    this.liveChartData.c = [];
    this.liveChartData.v = [];

    //Display list of open orders
    this.openOrderList = await this.getOpenOrder();

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

        this.liveChartData.c.push(parseFloat(this.coinData.c));
        //this.liveChartData.v.push(this.coinData.v);
        this.displayHighchart("");

      });
  }

  async bigBrother(lastPrice : number){
    if(lastPrice <= this.stopLose) (console.log("sold"));
    if(lastPrice <= this.openOrderList[0].orderStopLose) (console.log("sold AND TAKE YOUR LOOSE"))
  
   //re calculate stop loose
   this.stopLose = this.coinData.c - (lastPrice * this.stopLoseOffset)
  }

  async getOpenOrder() {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: location.origin + "/api/Order/GetOpenOrder/" + this.symbol,
    };
    return await this.httpService.xhr(httpSetting);
  }

  changeStopLoseOffset(type: string) {
    if (type == 'increase') { this.stopLoseOffset = this.stopLoseOffset + 0.0001 };
    if (type == 'reduce') { this.stopLoseOffset = this.stopLoseOffset - 0.0001 }
  }

  async displayHighchart(type: string) {

    this.liveChartData.color = [];

    //Init color
    this.liveChartData.c.map((data, index) => {
      this.liveChartData.color.push(0);
    });


    if (type == 'INIT') {
      // this.liveChartData = await this.getIntradayData(12, 5);
      this.liveChartData.color = [];

      //Init color
      this.liveChartData.c.map((data, index) => {
        this.liveChartData.color.push(0);
      });

      //Calculate min and max
      //this.initMaxMin(this.liveChartData);
    }

    //Display chart
    this.chartOptions2 = {
      series: [{ data: this.liveChartData.c, type: 'line' }],

      title: {
        text: 'Live'
      },
      xAxis: { type: 'datetime', dateTimeLabelFormats: { month: '%e. %b', year: '%b' }, title: { text: 'Date' } },
      yAxis: {
        plotLines: [{
          color: '#32CD32',
          width: 1,
          value: this.stopLose,
          label: {
            text: this.stopLoseOffset.toFixed(4).toString(),
            align: 'right'
          }
        },
        {
          color: '#FF0000',
          width: 2,
          value: this.openOrderList[0].orderStopLose,
          label: {
            text: this.openOrderList[0].orderStopLose,
            align: 'right'
          }
        }

        ]
        ,
      }
    };
  }

  ngOnDestroy() {
    this.service$.close();
    this.unsubscribe$.next();
    this.unsubscribe$.complete();
  }
}
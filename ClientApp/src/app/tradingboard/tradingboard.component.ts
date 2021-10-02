import { Component } from '@angular/core';
import { ActivatedRoute } from "@angular/router";
import { WebSocketService } from '../service/websocket.service';
import * as Highcharts from 'highcharts';
import { map, takeUntil } from 'rxjs/operators';
import { environment } from 'src/environments/environment';
import { Observable, Subject } from 'rxjs';

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

  liveChartData = [] as any;
  highcharts = Highcharts;
  highcharts2 = Highcharts;
  highcharts3 = Highcharts;
  chartOptions: Highcharts.Options;
  chartOptions2: Highcharts.Options;
  chartOptions3: Highcharts.Options;

  constructor(
    private route: ActivatedRoute,
    private service$: WebSocketService
  ) {
    this.unsubscribe$ = new Subject<void>();
    this.coinData = {};
  }

  async ngOnInit() {
    this.symbol = this.route.snapshot.paramMap.get("symbol");
    console.log(this.symbol);
    this.liveChartData.p = [];
    this.liveChartData.v = [];

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
        console.log("symbol data received");
        console.log(data);
        this.coinData = data;

        if (this.coinData.p < 0) this.coinData.color = "red";
        if (this.coinData.p >= 0) this.coinData.color = "limegreen";
        this.coinData.s.includes("USDT");
      });
  }

  ngOnDestroy() {
    console.log("destroy");
    this.service$.close();
    this.unsubscribe$.next();
    this.unsubscribe$.complete();
  }

  back(): void {
    // this.myWebSocket.close();  
  }

  // async displayHighchart(type: string) {
  //   if (type == 'INIT') {
  //    // this.liveChartData = await this.getIntradayData(12, 5);
  //     this.liveChartData.color = [];

  //     //Init color
  //     this.liveChartData.c.map((data, index) => {
  //       this.liveChartData.color.push(0);
  //     });

  //     //Calculate min and max
  //     //this.initMaxMin(this.liveChartData);
  //   }

  //   //Display chart
  //   this.chartOptions2 = {
  //     series: [{ data: this.liveChartData.c, type: 'line' }],
  //     title: {
  //       text: 'Live'
  //     },
  //     xAxis: { type: 'datetime', dateTimeLabelFormats: { month: '%e. %b', year: '%b' }, title: { text: 'Date' } },
  //   };
  // }
}
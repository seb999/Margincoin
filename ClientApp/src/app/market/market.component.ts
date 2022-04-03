import { Component } from '@angular/core';
import { WebSocket1Service } from '../service/websocket1.service';
import { map, takeUntil } from 'rxjs/operators';
import { environment } from 'src/environments/environment';
import { Observable, Subject } from 'rxjs';
import { HttpService, HttpSettings } from '../service/http.service';
export const WS_MARKET_ENDPOINT = environment.wsEndPointMarket;


@Component({
  selector: 'app-market',
  templateUrl: './market.component.html',
  providers: [WebSocket1Service]
})
export class MarketComponent {
  public symbolList$: Observable<any>;
  public marketDataListener$: Observable<any>;
  private unsubscribe$: Subject<void>;

  constructor(public service$: WebSocket1Service,
    private httpService: HttpService,) 
  {
    this.unsubscribe$ = new Subject<void>();
  }

  ngOnDestroy() { 
    this.service$.close();
    this.unsubscribe$.next();
    this.unsubscribe$.complete();
  }

  async ngOnInit() {
    var i = 29;
    this.marketDataListener$ = this.service$.connect(WS_MARKET_ENDPOINT).pipe(
      map(
        (response: MessageEvent): any => {
          let data = JSON.parse(response.data);
          return data;
        }
      )
    );

    this.marketDataListener$
      .pipe(takeUntil(this.unsubscribe$))
      .subscribe(data => {
        console.log("received data from market");
        i++;
        if (i == 30) {
          i = 0;
          this.symbolList$ = data.filter(function (symbol) {
            if (symbol.p < 0) symbol.color = "red";
            if (symbol.p >= 0) symbol.color = "limegreen";
            return symbol.s.includes("USDT"); 
          });
        };
      })
  };

  async monitorMarket(): Promise<any> {
    const httpSetting: HttpSettings = {
      method: 'GET',
      url: 'https://localhost:5001/api/AutoTrade2/MonitorMarket',
    };
    return await this.httpService.xhr(httpSetting);
  }
}
import { Component, OnInit } from '@angular/core';
import { SignalRService } from './service/signalR.service';
import { HttpClient } from '@angular/common/http';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html'
})
export class AppComponent {
  title = 'app';

  constructor(public signalRService: SignalRService, private http: HttpClient) { }
  
  ngOnInit() {
    this.signalRService.startConnection();
    this.signalRService.addTransferChartDataListener();   
    //this.startHttpRequest();
  }

  ngOnDestroy(){
    this.signalRService.closeConnection();
  }

  private startHttpRequest = () => {
    this.http.get('https://localhost:5001/api/AutoTrade/Activate/true')
      .subscribe(res => {
        console.log("sdfsdf");
      })
  }
}

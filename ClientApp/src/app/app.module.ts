import { BrowserModule } from '@angular/platform-browser';
import { NgModule } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClientModule } from '@angular/common/http';
import { RouterModule } from '@angular/router';

import { AppComponent } from './app.component';
import { NavMenuComponent } from './nav-menu/nav-menu.component';
import { WalletComponent } from './wallet/wallet.component';
import { OrderDetailComponent } from './orderDetail/orderDetail';

import { TradingboardComponent } from './tradingboard/tradingboard.component';
import { TradingboardHelper } from './tradingboard/tradingBoard.helper';
import { OrderDetailHelper } from './orderDetail/orderDetail.helper';
import { HighchartsChartModule } from 'highcharts-angular';
import { AppSetting } from './app.settings';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { NgbModule, NgbPopover, NgbCollapseModule } from '@ng-bootstrap/ng-bootstrap';
import { SettingsComponent } from './settings/settings.component';
import { PerformanceComponent } from './performance/performance.component';
import { AgGridModule } from 'ag-grid-angular';

@NgModule({
  declarations: [
    AppComponent,
    NavMenuComponent,
    WalletComponent,
    TradingboardComponent,
    OrderDetailComponent,
    SettingsComponent,
    PerformanceComponent
  ],
  imports: [
    MatSlideToggleModule,
    NgbModule,
    NgbCollapseModule,
    BrowserModule.withServerTransition({ appId: 'ng-cli-universal' }),
    HttpClientModule,
    FormsModule,
    HighchartsChartModule,
    AgGridModule,
    RouterModule.forRoot([
    { path: '', component: WalletComponent, pathMatch: 'full' },
    { path: 'trade', component: WalletComponent },
    { path: 'market', redirectTo: '', pathMatch: 'full' },
    { path: 'tradingboard/:symbol', component: TradingboardComponent },
    { path: 'orderDetail/:id', component: OrderDetailComponent },
    { path: 'settings', component: SettingsComponent },
    { path: 'performance', component: PerformanceComponent },
])
  ],
  providers: [TradingboardHelper, OrderDetailHelper,  AppSetting ],
  bootstrap: [AppComponent]
})
export class AppModule { }

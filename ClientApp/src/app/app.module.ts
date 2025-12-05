import { BrowserModule } from '@angular/platform-browser';
import { NgModule } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClientModule } from '@angular/common/http';
import { RouterModule } from '@angular/router';

import { AppComponent } from './app.component';
import { NavMenuComponent } from './nav-menu/nav-menu.component';
import { MarketComponent } from './market/market.component';
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
import { AIAnalyticsComponent } from './ai-analytics/ai-analytics.component';

@NgModule({
  declarations: [
    AppComponent,
    NavMenuComponent,
    MarketComponent,
    WalletComponent,
    TradingboardComponent,
    OrderDetailComponent,
    SettingsComponent,
    AIAnalyticsComponent
  ],
  imports: [
    MatSlideToggleModule,
    NgbModule,
    NgbCollapseModule,
    BrowserModule.withServerTransition({ appId: 'ng-cli-universal' }),
    HttpClientModule,
    FormsModule,
    HighchartsChartModule,
    RouterModule.forRoot([
    { path: '', component: MarketComponent, pathMatch: 'full' },
    { path: 'tradingboard/:symbol', component: TradingboardComponent },
    { path: 'wallet', component: WalletComponent },
    { path: 'orderDetail/:id', component: OrderDetailComponent },
    { path: 'settings', component: SettingsComponent },
    { path: 'ai-analytics', component: AIAnalyticsComponent },
])
  ],
  providers: [TradingboardHelper, OrderDetailHelper,  AppSetting ],
  bootstrap: [AppComponent]
})
export class AppModule { }

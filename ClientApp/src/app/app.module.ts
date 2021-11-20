import { BrowserModule } from '@angular/platform-browser';
import { NgModule } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClientModule } from '@angular/common/http';
import { RouterModule } from '@angular/router';

import { AppComponent } from './app.component';
import { NavMenuComponent } from './nav-menu/nav-menu.component';
import { MarketComponent } from './market/market.component';
import { WalletComponent } from './wallet/wallet.component';
import { TradingboardComponent } from './tradingboard/tradingboard.component';
import { TradingboardHelper } from './tradingboard/tradingBoard.helper';
import { HighchartsChartModule } from 'highcharts-angular';
import { AppSetting } from './app.settings';

@NgModule({
  declarations: [
    AppComponent,
    NavMenuComponent,
    MarketComponent,
    WalletComponent,
    TradingboardComponent,
    
  ],
  imports: [
    BrowserModule.withServerTransition({ appId: 'ng-cli-universal' }),
    HttpClientModule,
    FormsModule,
    HighchartsChartModule,
    RouterModule.forRoot([
    { path: '', component: MarketComponent, pathMatch: 'full' },
    { path: 'tradingboard/:symbol', component: TradingboardComponent },
    { path: 'wallet', component: WalletComponent },
], { relativeLinkResolution: 'legacy' })
  ],
  providers: [TradingboardHelper, AppSetting ],
  bootstrap: [AppComponent]
})
export class AppModule { }
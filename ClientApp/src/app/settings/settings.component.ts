import { Component, OnInit } from '@angular/core';
import { SettingsService, RuntimeSettings, StaticConfig } from '../service/settings.service';
import { HttpSettings, HttpService } from '../service/http.service';

export interface MemoryDiagnostics {
  timestamp: string;
  collections: {
    candleMatrix: {
      symbolCount: number;
      totalCandles: number;
      avgCandlesPerSymbol: number;
    };
    allMarketData: { count: number };
    marketStreamOnSpot: { count: number };
    onHold: { count: number; symbols: string[] };
    symbolWeTrade: { count: number };
    symbolBaseList: { count: number };
  };
  memory: {
    managedMemoryMB: number;
    gcCollections: {
      gen0: number;
      gen1: number;
      gen2: number;
    };
  };
}

@Component({
  selector: 'app-settings',
  templateUrl: './settings.component.html',
})
export class SettingsComponent implements OnInit {
  settings: RuntimeSettings;
  staticConfig: StaticConfig;
  diagnostics: MemoryDiagnostics | null = null;
  saving = false;
  message: string = '';
  error: string = '';
  showDiagnostics = false;

  constructor(
    private settingsService: SettingsService,
    private httpService: HttpService
  ) { }

  async ngOnInit() {
    await this.load();
  }

  async load() {
    this.error = '';
    this.message = '';
    try {
      // Load both runtime and static settings
      const [runtime, staticCfg] = await Promise.all([
        this.settingsService.getRuntimeSettings(),
        this.settingsService.getStaticConfig()
      ]);
      this.settings = runtime;
      this.staticConfig = staticCfg;
    } catch (err) {
      this.error = 'Failed to load settings';
      console.error('Settings load error:', err);
    }
  }

  async loadDiagnostics() {
    try {
      const httpSetting: HttpSettings = {
        method: 'GET',
        url: location.origin + '/api/Globals/GetMemoryDiagnostics',
      };
      this.diagnostics = await this.httpService.xhr(httpSetting);
      this.showDiagnostics = true;
    } catch (err) {
      this.error = 'Failed to load diagnostics';
      console.error('Diagnostics load error:', err);
    }
  }

  async save() {
    if (!this.settings) return;
    this.saving = true;
    this.error = '';
    this.message = '';
    try {
      this.settings = await this.settingsService.updateRuntimeSettings(this.settings);
      this.message = 'Runtime settings updated successfully';
      setTimeout(() => this.message = '', 3000);
    } catch (err) {
      this.error = 'Failed to save settings';
      console.error('Settings save error:', err);
    } finally {
      this.saving = false;
    }
  }
}

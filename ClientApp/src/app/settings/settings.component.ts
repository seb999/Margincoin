import { Component, OnInit } from '@angular/core';
import { SettingsService, TradingSettings } from '../service/settings.service';

@Component({
  selector: 'app-settings',
  templateUrl: './settings.component.html',
})
export class SettingsComponent implements OnInit {
  settings: TradingSettings;
  saving = false;
  message: string = '';
  error: string = '';

  constructor(private settingsService: SettingsService) { }

  async ngOnInit() {
    await this.load();
  }

  async load() {
    this.error = '';
    try {
      this.settings = await this.settingsService.getSettings();
    } catch (err) {
      this.error = 'Failed to load settings';
    }
  }

  async save() {
    if (!this.settings) return;
    this.saving = true;
    this.error = '';
    this.message = '';
    try {
      this.settings = await this.settingsService.updateSettings(this.settings);
      this.message = 'Settings updated';
    } catch (err) {
      this.error = 'Failed to save settings';
    } finally {
      this.saving = false;
    }
  }
}

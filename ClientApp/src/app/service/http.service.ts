import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';

@Injectable({
  providedIn: 'root'
})
export class HttpService {

  constructor(private http: HttpClient) { }

  public async xhr<T>(settings: HttpSettings): Promise<T> {
    if (!settings.method) {
      settings.method = 'GET';
    }

    switch (settings.method) {
      case 'GET':
        return this.http.get<T>(settings.url, {headers: this.getHeaders(settings.headers)}).toPromise();
      case 'POST':
        return this.http.post<T>(settings.url, settings.data, {headers: this.getHeaders(settings.headers)}).toPromise();
      case 'PUT':
        return this.http.put<T>(settings.url, settings.data, {headers: this.getHeaders(settings.headers)}).toPromise();
      case 'DELETE':
        return this.http.delete<T>(settings.url, {headers: this.getHeaders(settings.headers)}).toPromise();
    }
  }

  private getHeaders(headers: any): HttpHeaders {
    let httpHeaders: HttpHeaders = new HttpHeaders();

    if (headers !== undefined) {
      Object.keys(headers).forEach(key => {
        httpHeaders = httpHeaders.append(key, headers[key]);
      });
    }
    return httpHeaders;
  }
}

export interface HttpSettings {
  url: string;
  dataType?: string;
  method?: "GET" | "POST" | "PUT" | "DELETE";
  data?: any;
  headers?: any;
}
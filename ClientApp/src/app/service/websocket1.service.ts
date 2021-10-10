import { Injectable } from '@angular/core';
import { Observable, Observer, Subject } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class WebSocket1Service {
  private ws: WebSocket;
  private subject: Subject<MessageEvent>;

  public connect(wsEndpoint): Subject<MessageEvent> {
    if (!this.subject || this.subject.closed) {
      this.subject = this.create(wsEndpoint);
      console.log("Successfully connected");
    }
    return this.subject;
  }

  private create(url): Subject<MessageEvent> {

    this.ws = new WebSocket(url);
    const observable = Observable.create((observer: Observer<MessageEvent>) => {
      this.ws.onmessage = observer.next.bind(observer);
      this.ws.onerror = observer.error.bind(observer);
      this.ws.onclose = observer.complete.bind(observer);
      return this.ws.close.bind(this.ws);
    });

    let observer = {
      next: (data: Object) => {
        if (this.ws.readyState === WebSocket.OPEN) {
          this.ws.send(JSON.stringify(data));
        }
      }
    };
    return Subject.create(observer, observable);
  }

  public close() {
    this.subject.unsubscribe();
  }
}
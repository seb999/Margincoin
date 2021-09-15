import { Injectable, EventEmitter } from '@angular/core';
import { AppGlobal } from "./../app.settings";

@Injectable({
    providedIn: 'root'
})
export class WebsocketService {
    public eventMessage: EventEmitter<string> = new EventEmitter();
    constructor(private appGlobal: AppGlobal) {
    }

    public onMessage() {
        return this.eventMessage;
    }

    openListener(forceStop) {
        this.webSocket(this.eventMessage, forceStop);
    }

    //We have to pass to the websocket the eventMessage
    webSocket(eventMessage: EventEmitter<string>, forceStop) {

        var socket = new WebSocket('wss://ws.finnhub.io?token=' + this.appGlobal.finnhubKey);

        if (forceStop) {
            socket.close();
        }
        else {
            socket.addEventListener('open', function (event) {
                socket.send(JSON.stringify({ 'type': 'subscribe', 'symbol': 'OANDA:WTICO_USD' }))
            });

            socket.addEventListener('message', function (event) {
                console.log(event.data);
                if (JSON.parse(event.data).type != 'ping') {
                    return eventMessage.emit(JSON.parse(event.data));
                }
            });
        }
    }
}
import { EventEmitter, Injectable } from '@angular/core';
import * as signalR from "@microsoft/signalr";
import { ServerMsg } from '../class/serverMsg';
import { BackEndMessage } from '../class/enum';

@Injectable({
    providedIn: 'root'
})
export class SignalRService {
    public data: any;
    private hubConnection: signalR.HubConnection;
    public eventMessage: EventEmitter<ServerMsg> = new EventEmitter();

    public startConnection = () => {
        this.hubConnection = new signalR.HubConnectionBuilder()
            .withUrl(location.origin + '/Signalr')
            .withAutomaticReconnect()
            .build();

        this.hubConnection
            .start()
            .then(() => console.log('Connection started'))
            .catch(err => console.log('Error while starting SignalR connection: ' + err))
    }

    public onMessage() {
        return this.eventMessage;
    }

    public closeConnection = () => {
        this.hubConnection.stop();
    }

    public openDataListener = () => {

        this.hubConnection.on(BackEndMessage.trading, (data) => {
            let serverMsg: ServerMsg = {
                msgName: BackEndMessage.trading,
                candleList: JSON.parse(data)
            }
            return this.eventMessage.emit(serverMsg);
        });

        this.hubConnection.on(BackEndMessage.newPendingOrder, (data) => {
            let serverMsg: ServerMsg = {
                msgName: BackEndMessage.newPendingOrder,
                order: JSON.parse(data)
            };
            return this.eventMessage.emit(serverMsg);
        });

        this.hubConnection.on(BackEndMessage.sellOrderFilled, (data) => {
            let serverMsg: ServerMsg = {
                msgName: BackEndMessage.sellOrderFilled,
                order: JSON.parse(data)
            };
            return this.eventMessage.emit(serverMsg);
        });

        this.hubConnection.on(BackEndMessage.buyOrderFilled, (data) => {
            let serverMsg: ServerMsg = {
                msgName: BackEndMessage.buyOrderFilled,
                order: JSON.parse(data)
            };
            return this.eventMessage.emit(serverMsg);
        });

        this.hubConnection.on(BackEndMessage.newOrder, () => {
            let serverMsg: ServerMsg = { msgName: BackEndMessage.newOrder };
            return this.eventMessage.emit(serverMsg);
        });

        this.hubConnection.on(BackEndMessage.webSocketStopped, () => {
            let serverMsg: ServerMsg = { msgName: BackEndMessage.webSocketStopped };
            return this.eventMessage.emit(serverMsg);
        });

        this.hubConnection.on(BackEndMessage.httpRequestError, (error) => {
            console.log(error);
            let serverMsg: ServerMsg = { 
                msgName: BackEndMessage.httpRequestError,
                httpError : error};
            return this.eventMessage.emit(serverMsg);
        });

        this.hubConnection.on(BackEndMessage.exportChart, (data) => {
            let serverMsg: ServerMsg = {
                msgName: BackEndMessage.exportChart,
                tradeSymbolList: JSON.parse(data)
            };
            return this.eventMessage.emit(serverMsg);
        });
    }
}

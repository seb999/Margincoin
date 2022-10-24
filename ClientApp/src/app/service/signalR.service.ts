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
            .withUrl(location.origin +'/Signalr')
            .build();
        this.hubConnection
            .start()
            .then(() => console.log('Connection started'))
            .catch(err => console.log('Error while starting SignalR connection: ' + err))
    }

    public onMessage() {
        return this.eventMessage;
      }

    public closeConnection = () =>{
        this.hubConnection.stop();
    }
    
    public openDataListener = () => {

        this.hubConnection.on(BackEndMessage.trading, (candleList) => {       
            let serverMsg : ServerMsg = {
                msgName : BackEndMessage.trading,
                candleList : JSON.parse(candleList)
            }
            return this.eventMessage.emit(serverMsg);
        });

        this.hubConnection.on(BackEndMessage.newOrder, () => {
            let serverMsg :ServerMsg = {
                msgName : BackEndMessage.newOrder
            };
            return this.eventMessage.emit(serverMsg);
        });

        this.hubConnection.on(BackEndMessage.binanceAccessFaulty, () => {
            let serverMsg :ServerMsg = {
                msgName : BackEndMessage.binanceAccessFaulty
            };
            return this.eventMessage.emit(serverMsg);
        });

        this.hubConnection.on(BackEndMessage.binanceTooManyRequest, () => {
            let serverMsg :ServerMsg = {
                msgName : BackEndMessage.binanceTooManyRequest
            };
            return this.eventMessage.emit(serverMsg);
        });

        this.hubConnection.on(BackEndMessage.binanceCheckAllowedIP, () => {
            let serverMsg :ServerMsg = {
                msgName : BackEndMessage.binanceCheckAllowedIP
            };
            return this.eventMessage.emit(serverMsg);
        });
    } 
}
import { EventEmitter, Injectable } from '@angular/core';
import * as signalR from "@microsoft/signalr";
import { Subject } from '@microsoft/signalr';
import { ServerMsg } from '../class/serverMsg';

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
    
    public addTransferChartDataListener = () => {
        this.hubConnection.on('trading', (candleList) => {       
            let serverMsg : ServerMsg = {
                msgName : 'trading',
                candleList : JSON.parse(candleList)
            }
            return this.eventMessage.emit(serverMsg);
        });

        this.hubConnection.on('newOrder', () => {
            let serverMsg :ServerMsg = {
                msgName : 'newOrder'
            };
            return this.eventMessage.emit(serverMsg);
        });

        this.hubConnection.on('binanceAccessFaulty', () => {
            console.log('binanceAccessFaulty');
            let serverMsg :ServerMsg = {
                msgName : 'binanceAccessFaulty'
            };
            return this.eventMessage.emit(serverMsg);
        });

        this.hubConnection.on('binanceTooManyRequest', () => {
            let serverMsg :ServerMsg = {
                msgName : 'binanceTooManyRequest'
            };
            return this.eventMessage.emit(serverMsg);
        });

        this.hubConnection.on('binanceCheckAllowedIP', () => {
            let serverMsg :ServerMsg = {
                msgName : 'binanceCheckAllowedIP'
            };
            return this.eventMessage.emit(serverMsg);
        });

        
    }

    //client subscribe to this event
    public onMessage() {
        return this.eventMessage;
      }

    public closeConnection = () =>{
        this.hubConnection.stop();
    }
}
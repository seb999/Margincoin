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
            .withUrl('https://localhost:5002/Signalr')
            .build();
        this.hubConnection
            .start()
            .then(() => console.log('Connection started'))
            .catch(err => console.log('Error while starting SignalR connection: ' + err))
    }
    
    public addTransferChartDataListener = () => {
        this.hubConnection.on('trading', (rsi,r1,s1) => {       
            let serverMsg : ServerMsg = {
                msgName : 'trading',
                rsi : rsi,
                r1 : r1,
                s1 : s1
            }
            return this.eventMessage.emit(serverMsg);
        });

        this.hubConnection.on('refreshUI', () => {
            let serverMsg :ServerMsg = {
                msgName : 'refreshUI'
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
import { EventEmitter, Injectable } from '@angular/core';
import * as signalR from "@microsoft/signalr";
import { Subject } from '@microsoft/signalr';

@Injectable({
    providedIn: 'root'
})
export class SignalRService {
    public data: any;
    private hubConnection: signalR.HubConnection;
    public eventMessage: EventEmitter<string> = new EventEmitter();

    public startConnection = () => {
        this.hubConnection = new signalR.HubConnectionBuilder()
            .withUrl('https://localhost:5001/Signalr')
            .build();
        this.hubConnection
            .start()
            .then(() => console.log('Connection started'))
            .catch(err => console.log('Error while starting SignalR connection: ' + err))
    }
    
    public addTransferChartDataListener = () => {
        console.log("opening listener");
        this.hubConnection.on('trading', () => {
            return this.eventMessage.emit('trading');
        });

        this.hubConnection.on('refreshUI', () => {
            return this.eventMessage.emit('refreshUI');
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
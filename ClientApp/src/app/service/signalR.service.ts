import { Injectable } from '@angular/core';
import * as signalR from "@microsoft/signalr";
@Injectable({
    providedIn: 'root'
})
export class SignalRService {
    public data: any;
    private hubConnection: signalR.HubConnection
    
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
        this.hubConnection.on('newOrder', (data) => {
            this.data = data;
            console.log("YEAAAAAAAAAAA");
        });
    }
}
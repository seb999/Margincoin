export class Order {
  constructor(
    public id : number,
    public symbol: string,
    public amount?: number,
    public quantityBuy?: number,
    public quantitySell?: number,
    public openPrice?: number,
    public highPrice?: number,
    public lowPrice?: number,
    public closePrice?: number,
    public stopLose?: number,
    public takeProfit?: number,
    public profit?: number,
    public fee?: number,
    public margin? : number,
    public openDate? : string,
    public closeDate? : string,
    public isClosed?: number,
    )
    {}
}
export class Order {
  constructor(
    public id : number,
    public symbol: string,
    public amount?: number,
    public quantity?: number,
    public openPrice?: number,
    public stopLose?: number,
    public margin? : number,
    public openDate? : string,
    public closeDate? : string,
    )
    {}
}
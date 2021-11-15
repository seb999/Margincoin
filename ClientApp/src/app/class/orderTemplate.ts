export class OrderTemplate {
  constructor(
    public id : number,
    public symbol: string,
    public amount?: number,
    public margin? : number,
    public stopLose?: number,
    public openDate? : string,
    public closeDate? : string,
    )
    {}
}
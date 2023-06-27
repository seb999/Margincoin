export class ServerMsg {
    constructor(
      public msgName: string,
      public candleList? : any[],
      public order? : any,
      public tradeSymbolList? : string[],
      )
      {}
  }
export class ServerMsg {
    constructor(
      public msgName: string,
      public httpError?: string,
      public candleList? : any[],
      public order? : any,
      public tradeSymbolList? : string[],
      public data? : any,
      )
      {}
  }
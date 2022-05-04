export class ServerMsg {
    constructor(
      public msgName: string,
      public symbolWeight? : any,
      public rsi? : number,
      public r1? : number,
      public s1? : number,
      )
      {}
  }
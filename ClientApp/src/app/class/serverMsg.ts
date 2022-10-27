import { Test } from '../class/Test';
export class ServerMsg {
    constructor(
      public msgName: string,
      public candleList? : any[],
      public order? : any,
      )
      {}
  }
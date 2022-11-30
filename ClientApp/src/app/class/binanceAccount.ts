export class BinanceAccount {
  constructor(
    public makerCommission : number,
    public takerCommission : number,
    public buyerCommission : number,
    public sellerCommission : number,
    public canTrade: boolean,
    public canWithdraw: boolean,
    public canDeposit: boolean,
    public brokered: boolean,
    public accountType: string,
    public balances : any[]
  
    )
    {}
}
export class BinanceOrder {
  constructor(
    public orderId : number,
    public symbol: string,
    public price: string,
    public origQty: string,
    public executedQty: string,
    public cummulativeQuoteQty: string,
    public status: string,
    public type?: string,
    public side?: string
    )
    {}
}
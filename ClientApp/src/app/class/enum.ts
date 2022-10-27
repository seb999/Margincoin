
export enum BackEndMessage
{
    trading = 'trading',
    newPendingOrder = 'newPendingOrder',
    newOrder = 'newOrder',
    binanceAccessFaulty = 'binanceAccessFaulty',
    binanceTooManyRequest = 'binanceTooManyRequest',
    binanceCheckAllowedIP = 'binanceCheckAllowedIP'
};
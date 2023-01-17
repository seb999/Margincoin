
export enum BackEndMessage {
  trading = 'trading',
  newPendingOrder = 'newPendingOrder',
  newOrder = 'newOrder',
  apiAccessFaulty = 'BinanceAccessFaulty',
  apiTooManyRequest = 'BinanceTooManyRequest',
  apiCheckAllowedIP = 'BinanceCheckAllowedIP',
  sellOrderFilled = 'sellOrderFilled',
  sellOrderExired = 'sellOrderExired',
  exportChart = 'exportChart',
  webSocketStopped = 'WebSocketStopped'
};

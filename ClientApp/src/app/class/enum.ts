
export enum BackEndMessage {
  trading = 'trading',
  newPendingOrder = 'newPendingOrder',
  newOrder = 'newOrder',
  apiAccessFaulty = 'BinanceAccessFaulty',
  apiTooManyRequest = 'BinanceTooManyRequest',
  apiCheckAllowedIP = 'BinanceCheckAllowedIP',
  sellOrderFilled = 'sellOrderFilled',
  buyOrderFilled = 'buyOrderFilled',
  sellOrderExired = 'sellOrderExired',
  buyOrderExired = 'buyOrderExired',
  exportChart = 'exportChart',
  webSocketStopped = 'WebSocketStopped'
};

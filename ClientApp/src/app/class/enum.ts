
export enum BackEndMessage {
  trading = 'trading',
  newPendingOrder = 'newPendingOrder',
  refreshUI = 'refreshUI',
  exportChart = 'exportChart',
  webSocketStopped = 'WebSocketStopped',
  sellOrderFilled = 'sellOrderFilled',
  buyOrderFilled = 'buyOrderFilled',
  httpRequestError = 'httpRequestError',
};

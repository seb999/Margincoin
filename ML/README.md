# LSTM/Transformer Trading Model

Advanced time-series prediction model for cryptocurrency trading using hybrid LSTM/Transformer architecture.

## Overview

This ML system replaces the image-based classification approach with a more powerful time-series model that:

- **Processes raw numerical data** (no image conversion needed)
- **Captures temporal dependencies** with LSTM and Transformer layers
- **Provides dual outputs**: Classification (UP/DOWN/SIDEWAYS) + Regression (expected return %)
- **Offers interpretability** via attention weights
- **Integrates seamlessly** with the existing C# trading application

## Architecture

```
Input (50 timesteps × ~35 features)
    ↓
Input Projection + Layer Norm
    ↓
Positional Encoding
    ↓
Transformer Encoder (2 layers)
    ↓
Bidirectional LSTM (2 layers)
    ↓
Multi-Head Attention
    ↓
    ├─→ Classification Head → UP/DOWN/SIDEWAYS
    └─→ Regression Head → Expected Return %
```

## Features Used

### Core OHLCV
- Open, High, Low, Close, Volume

### Technical Indicators (from C#)
- RSI (14)
- MACD, MACD Signal, MACD Histogram
- EMA50
- Stochastic K & D

### Engineered Features (calculated by preprocessor)
- Returns, log returns
- Price ratios (HL, CO)
- Volatility (5 & 20 periods)
- EMA distance & slope
- MACD acceleration & histogram slope
- RSI momentum & moving average
- Volume features
- Candle patterns (body, shadows)
- Price position in range
- Momentum indicators

**Total: ~35 features**

## Setup

### 1. Install Python Dependencies

```bash
cd ML
pip install -r requirements.txt
```

For indicator calculation (optional but recommended):
```bash
pip install pandas-ta
```

### 2. Collect Training Data

#### Option A: From Binance API (Quick Start)
```bash
cd trading_model/utils
python collect_data.py \
    --source binance \
    --symbols BTCUSDT ETHUSDT BNBUSDT ADAUSDT SOLUSDT \
    --interval 30m \
    --limit 1000 \
    --output ../data/training_data.csv
```

#### Option B: From C# Application Export
1. Export candle data from your C# app to JSON:
```csharp
// In your C# code:
var exportData = _tradingState.CandleMatrix.Select(candles => new {
    symbol = candles.First().s,
    candles = candles.Select(c => new {
        c.o, c.h, c.l, c.c, c.v,
        c.Rsi, c.Macd, c.MacdSign, c.MacdHist,
        c.Ema, c.StochSlowK, c.StochSlowD
    })
});
File.WriteAllText("candle_export.json", JsonSerializer.Serialize(exportData));
```

2. Convert to CSV:
```bash
python collect_data.py \
    --source json \
    --json-path /path/to/candle_export.json \
    --output ../data/training_data.csv
```

### 3. Train the Model

```bash
cd trading_model
python train.py
```

Configuration in `train.py`:
- `MODEL_TYPE`: 'transformer_lstm' or 'lightweight_lstm'
- `BATCH_SIZE`: 32 (default)
- `EPOCHS`: 100 (with early stopping)
- `LEARNING_RATE`: 0.001

Training outputs:
- `checkpoints/best_model.pt` - Best model weights
- `checkpoints/preprocessor.pkl` - Fitted scaler and feature config
- `checkpoints/history.json` - Training metrics

### 4. Start the Prediction API

```bash
cd trading_model/api
python prediction_service.py
```

The API will start on `http://localhost:8000`

Test it:
```bash
curl http://localhost:8000/health
```

### 5. Configure C# Application

Add to your `Startup.cs` or `Program.cs`:

```csharp
// Register HttpClient for ML service
services.AddHttpClient();

// Register LSTM prediction service instead of old ML service
services.AddSingleton<IMLService, LSTMPredictionService>();
```

Set environment variable (optional):
```bash
export ML_API_URL=http://localhost:8000
```

Or in `appsettings.json`:
```json
{
  "MLService": {
    "ApiUrl": "http://localhost:8000"
  }
}
```

## Usage

### From C# Application

The integration is automatic! The `LSTMPredictionService` implements `IMLService`, so your existing code works without changes:

```csharp
// Existing code continues to work:
var prediction = _mlService.MLPredList.FirstOrDefault(p => p.Symbol == "BTCUSDT");

if (prediction != null)
{
    Console.WriteLine($"Prediction: {prediction.PredictedLabel}");
    Console.WriteLine($"Confidence: {prediction.Confidence:P}");
    Console.WriteLine($"Expected Return: {prediction.ExpectedReturn:P2}");
    Console.WriteLine($"Trend Score: {prediction.TrendScore}");
}
```

### Direct API Usage

```bash
curl -X POST http://localhost:8000/predict \
  -H "Content-Type: application/json" \
  -d '{
    "symbol": "BTCUSDT",
    "candles": [
      {
        "open": 50000, "high": 51000, "low": 49500, "close": 50500,
        "volume": 1000, "rsi": 55, "macd": 0.5,
        "macdSign": 0.3, "macdHist": 0.2, "ema": 50200,
        "stochSlowK": 60, "stochSlowD": 55
      },
      ... (49 more candles)
    ]
  }'
```

Response:
```json
{
  "symbol": "BTCUSDT",
  "prediction": "UP",
  "confidence": 0.85,
  "probabilities": {
    "down": 0.05,
    "sideways": 0.10,
    "up": 0.85
  },
  "expected_return": 0.012,
  "trend_score": 4,
  "attention_summary": {
    "t-1": 0.15,
    "t-2": 0.12,
    "t-5": 0.10
  }
}
```

## Model Performance

Expected metrics (after training on sufficient data):

- **Accuracy**: 65-75% (vs 55-65% for image model)
- **UP class precision**: 70-80%
- **Inference latency**: 5-20ms (vs 50-200ms for image model)
- **F1-score**: 0.65-0.75

## API Endpoints

### `GET /` or `/health`
Health check
```json
{
  "status": "healthy",
  "model": "loaded",
  "device": "cuda",
  "input_features": 35
}
```

### `POST /predict`
Make prediction (see usage above)

### `GET /model/info`
Get model configuration
```json
{
  "model_type": "TransformerLSTMModel",
  "input_features": 35,
  "feature_names": [...],
  "lookback": 50
}
```

## Production Deployment

### Option 1: Local Process
Run the API as a background service:
```bash
nohup python api/prediction_service.py > ml_api.log 2>&1 &
```

### Option 2: Docker
```dockerfile
FROM python:3.10-slim

WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY trading_model/ ./trading_model/
WORKDIR /app/trading_model/api

CMD ["python", "prediction_service.py"]
```

Build and run:
```bash
docker build -t trading-ml-api .
docker run -d -p 8000:8000 trading-ml-api
```

### Option 3: ONNX Export (Fastest)
For lowest latency, export to ONNX and run inference in C#:

```python
# Export script
import torch
import torch.onnx

model.eval()
dummy_input = torch.randn(1, 50, 35)

torch.onnx.export(
    model,
    dummy_input,
    "trading_model.onnx",
    input_names=['input'],
    output_names=['class_logits', 'regression'],
    dynamic_axes={'input': {0: 'batch'}}
)
```

Then use `Microsoft.ML.OnnxRuntime` in C#.

## Monitoring & Retraining

### Track Performance
Monitor prediction accuracy in production:
```csharp
// Log actual vs predicted outcomes
_logger.LogInformation(
    "Trade result: Predicted={Predicted}, Actual={Actual}, Return={Return}",
    prediction.PredictedLabel,
    actualDirection,
    actualReturn
);
```

### Retrain Schedule
- **Weekly**: Incremental training with recent data
- **Monthly**: Full retraining from scratch
- **On demand**: After significant market regime changes

### Model Versioning
```
checkpoints/
├── v1_2024-01-15/
│   ├── best_model.pt
│   └── preprocessor.pkl
├── v2_2024-02-01/
│   ├── best_model.pt
│   └── preprocessor.pkl
└── current/ -> v2_2024-02-01/
```

## Troubleshooting

### API not responding
```bash
# Check if running
curl http://localhost:8000/health

# Check logs
tail -f ml_api.log

# Restart
pkill -f prediction_service
python api/prediction_service.py
```

### Model not found error
```bash
# Ensure you trained the model first
ls checkpoints/

# Should see:
# best_model.pt
# preprocessor.pkl
# history.json
```

### Low accuracy
- **Collect more data** (aim for 10,000+ sequences)
- **Increase model size** (hidden_size=256, num_layers=3)
- **Tune hyperparameters** (learning rate, dropout)
- **Check data quality** (ensure indicators are calculated correctly)

### C# integration issues
```csharp
// Test connection
var service = new LSTMPredictionService(logger, httpClientFactory);
var isHealthy = await service.IsHealthyAsync();
Console.WriteLine($"ML API healthy: {isHealthy}");
```

## Next Steps

1. ✅ **Trend Score Integration**: Works alongside ML predictions
2. 🔄 **Collect Historical Data**: Gather 2-3 months of data
3. 🔄 **Train Initial Model**: Start with lightweight_lstm
4. 🔄 **Deploy API**: Run prediction service
5. 🔄 **Monitor Performance**: Track accuracy in live trading
6. 🔄 **Optimize & Retrain**: Improve based on results

## License

Part of MarginCoin trading system.


./venv/bin/python trading_model/utils/collect_data.py \
  --start-date 2024-06-01 \
  --end-date 2024-12-01 \
  --symbols BTCUSDT ETHUSDT \
  --interval 1h \
  --output data/training_data.csv


  ./venv/bin/python trading_model/utils/collect_data.py \
  --months 12 \
  --symbols BTCUSDT ETHUSDT BNBUSDT SOLUSDT \
  --interval 30m \
  --output data/training_data.csv

# ML Trading Model - Quick Start Guide

## Environment Setup ✓

Your Python environment is ready at `ML/venv/`

**Python Version:** 3.12.8

**Installed Packages:**
- PyTorch 2.9.1
- NumPy 2.2.6
- Pandas 2.3.3
- Scikit-learn 1.7.2
- FastAPI 0.122.0
- Pandas-TA 0.4.71b0
- And all dependencies

## Usage

### 1. Activate the Environment

```bash
cd ML
python -m venv venv
source venv/bin/activate
pip install -r requirements.txt
pyhton /trading_model/train.py
```

Or manually:
```bash
source venv/bin/activate
```

### 2. Collect Training Data

**RECOMMENDED: Fetch multi-year data from Binance**

Collect 5 years of data covering both bull and bear markets. The training script automatically handles class imbalance using weighted loss:

```bash
# Fetch 5 years of data from Binance (covers both bull and bear markets)
./venv/bin/python trading_model/utils/collect_data.py \
  --start-date 2020-01-01 \
  --end-date 2025-01-01 \
  --symbols BTCUSDT ETHUSDT BNBUSDT SOLUSDT \
  --interval 30m \
  --output data/training_data.csv
```

**Alternative: Quick test with 6 months (not recommended for production)**
```bash
./venv/bin/python trading_model/utils/collect_data.py \
  --months 6 \
  --symbols BTCUSDT ETHUSDT BNBUSDT \
  --interval 30m \
  --output data/training_data.csv
```

**Note:** The model uses class-weighted loss to automatically handle imbalanced data. You don't need to manually balance the dataset.

**Option B: Export from C# Application**

Export your candle data from C# to JSON format, then:
```bash
./venv/bin/python trading_model/utils/collect_data.py \
  --source json \
  --json-path path/to/candles.json \
  --output data/training_data.csv
```

Expected JSON format:
```json
[
  {
    "symbol": "BTCUSDT",
    "candles": [
      {
        "o": 50000, "h": 51000, "l": 49500, "c": 50500,
        "v": 1000, "Rsi": 55, "Macd": 0.5,
        "MacdSign": 0.3, "MacdHist": 0.2, "Ema": 50200,
        "StochSlowK": 60, "StochSlowD": 55
      }
    ]
  }
]
```

### 3. Train the Model

```bash
cd ML/trading_model
../venv/bin/python train.py
```

This will:
- Load the training data from `data/training_data.csv`
- Create features and labels automatically
- Train the LSTM/Transformer model
- Save the best model to `checkpoints/best_model.pt`
- Save the preprocessor to `checkpoints/preprocessor.pkl`

**Training Configuration (in train.py):**
- Model: TransformerLSTM (or LightweightLSTM)
- Lookback: 50 candles
- Batch size: 32
- Epochs: 100 (with early stopping)
- Device: GPU if available, otherwise CPU

### 4. Start the Prediction API

```bash
cd ML/trading_model
../venv/bin/python api/prediction_service.py
```

The API will start at `http://localhost:8000`

**Endpoints:**
- `GET /` - Health check
- `GET /health` - Detailed health check
- `POST /predict` - Get prediction for a symbol
- `GET /model/info` - Get model information

**Example API Request:**
```bash
curl -X POST http://localhost:8000/predict \
  -H "Content-Type: application/json" \
  -d '{
    "symbol": "BTCUSDT",
    "candles": [
      {
        "open": 50000,
        "high": 51000,
        "low": 49500,
        "close": 50500,
        "volume": 1000,
        "rsi": 55,
        "macd": 0.5,
        "macdSign": 0.3,
        "macdHist": 0.2,
        "ema": 50200,
        "stochSlowK": 60,
        "stochSlowD": 55
      },
      ...
    ]
  }'
```

**Response:**
```json
{
  "symbol": "BTCUSDT",
  "prediction": "UP",
  "confidence": 0.75,
  "probabilities": {
    "down": 0.1,
    "sideways": 0.15,
    "up": 0.75
  },
  "expected_return": 0.025,
  "trend_score": 3,
  "attention_summary": {
    "t-1": 0.25,
    "t-2": 0.15,
    "t-5": 0.12
  }
}
```

## Project Structure

```
ML/
├── venv/                      # Python virtual environment
├── data/                      # Training data (created on first run)
├── checkpoints/               # Saved models (created during training)
├── trading_model/
│   ├── train.py              # Training script
│   ├── models/
│   │   └── transformer_lstm.py   # Model architectures
│   ├── utils/
│   │   ├── collect_data.py       # Data collection
│   │   └── preprocessor.py       # Feature engineering
│   └── api/
│       └── prediction_service.py # FastAPI service
├── requirements.txt          # Python dependencies
├── activate.sh              # Environment activation helper
└── QUICKSTART.md           # This file
```

## Troubleshooting

**Import errors:**
```bash
# Make sure you're in the correct directory
cd ML/trading_model
# Use the venv python
../venv/bin/python train.py
```

**No data error:**
```bash
# Collect data first
cd ML
./venv/bin/python trading_model/utils/collect_data.py --months 6
```

**Model not found error (when starting API):**
```bash
# Train the model first
cd ML/trading_model
../venv/bin/python train.py
```

## Next Steps

1. Collect sufficient training data (at least 3-6 months recommended)
2. Train the model and monitor validation accuracy
3. Start the prediction API
4. Integrate the API with your C# application
5. Monitor predictions and retrain periodically with new data

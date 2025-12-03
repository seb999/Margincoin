"""
FastAPI service for real-time trading predictions
Provides HTTP endpoint for C# application to get ML predictions
"""

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field
from typing import List, Dict, Optional
import torch
import numpy as np
from pathlib import Path
import logging

# Add parent directory to path
import sys
sys.path.append(str(Path(__file__).parent.parent))

from models.transformer_lstm import create_model
from utils.preprocessor import TradingDataPreprocessor, prepare_data_from_candles

# Setup logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Initialize FastAPI app
app = FastAPI(
    title="Trading ML Prediction Service",
    description="LSTM/Transformer model for cryptocurrency trading predictions",
    version="1.0.0"
)

# Global model and preprocessor (loaded on startup)
model = None
preprocessor = None
device = None


class CandleData(BaseModel):
    """Single candle data point"""
    open: float
    high: float
    low: float
    close: float
    volume: Optional[float] = 0
    rsi: Optional[float] = 50
    macd: Optional[float] = 0
    macd_signal: Optional[float] = Field(default=0, alias='macdSign')
    macd_hist: Optional[float] = Field(default=0, alias='macdHist')
    ema50: Optional[float] = Field(default=None, alias='ema')
    stoch_k: Optional[float] = Field(default=50, alias='stochSlowK')
    stoch_d: Optional[float] = Field(default=50, alias='stochSlowD')

    class Config:
        populate_by_name = True  # Allow both 'macd_signal' and 'macdSign'


class PredictionRequest(BaseModel):
    """Request format for predictions"""
    symbol: str
    candles: List[CandleData] = Field(..., min_items=50, description="Minimum 50 candles required")


class PredictionResponse(BaseModel):
    """Response format for predictions"""
    symbol: str
    prediction: str  # "UP", "DOWN", or "SIDEWAYS"
    confidence: float  # 0.0 to 1.0
    probabilities: Dict[str, float]  # {"down": 0.1, "sideways": 0.2, "up": 0.7}
    expected_return: float  # Predicted return percentage
    trend_score: Optional[int] = None  # Optional: calculated trend score
    attention_summary: Optional[Dict[str, float]] = None  # Which timeframes were most important


@app.on_event("startup")
async def load_model():
    """Load model and preprocessor on startup"""
    global model, preprocessor, device

    logger.info("Loading model and preprocessor...")

    try:
        # Set device
        device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
        logger.info(f"Using device: {device}")

        # Load preprocessor
        preprocessor_path = Path('checkpoints/preprocessor.pkl')
        if not preprocessor_path.exists():
            raise FileNotFoundError(f"Preprocessor not found at {preprocessor_path}")

        preprocessor = TradingDataPreprocessor.load(str(preprocessor_path))
        logger.info(f"Loaded preprocessor with {len(preprocessor.feature_columns)} features")

        # Load model checkpoint
        checkpoint_path = Path('checkpoints/best_model.pt')
        if not checkpoint_path.exists():
            raise FileNotFoundError(f"Model checkpoint not found at {checkpoint_path}")

        checkpoint = torch.load(checkpoint_path, map_location=device)

        # Create model with same architecture
        input_size = len(preprocessor.feature_columns)

        # Prefer metadata stored in checkpoint, fall back to legacy defaults
        model_type = checkpoint.get('model_type', 'transformer_lstm')
        legacy_default_kwargs = {
            'transformer_lstm': {
                'hidden_size': 128,
                'num_lstm_layers': 2,
                'num_transformer_layers': 2,
                'num_heads': 4,
                'dropout': 0.2
            },
            'lightweight_lstm': {
                'hidden_size': 64,
                'num_layers': 2,
                'dropout': 0.2
            }
        }
        model_kwargs = checkpoint.get(
            'model_kwargs',
            legacy_default_kwargs.get(model_type, {})
        )
        logger.info(f"Loading model_type={model_type} with kwargs={model_kwargs}")

        model = create_model(
            model_type=model_type,
            input_size=input_size,
            **model_kwargs
        )

        model.load_state_dict(checkpoint['model_state_dict'])
        model = model.to(device)
        model.eval()

        logger.info(f"Model loaded successfully!")
        logger.info(f"  Validation accuracy: {checkpoint.get('val_accuracy', 'N/A')}")
        logger.info(f"  Validation F1: {checkpoint.get('val_f1', 'N/A')}")

    except Exception as e:
        logger.error(f"Failed to load model: {e}")
        raise


@app.get("/")
async def root():
    """Health check endpoint"""
    return {
        "status": "running",
        "model_loaded": model is not None,
        "device": str(device),
        "features": len(preprocessor.feature_columns) if preprocessor else 0
    }


@app.get("/health")
async def health_check():
    """Detailed health check"""
    if model is None or preprocessor is None:
        raise HTTPException(status_code=503, detail="Model not loaded")

    return {
        "status": "healthy",
        "model": "loaded",
        "device": str(device),
        "input_features": len(preprocessor.feature_columns),
        "lookback": preprocessor.lookback
    }


@app.post("/predict", response_model=PredictionResponse)
async def predict(request: PredictionRequest):
    """
    Make prediction for a symbol based on recent candles

    Args:
        request: PredictionRequest with symbol and candle data

    Returns:
        PredictionResponse with prediction, confidence, and probabilities
    """
    if model is None or preprocessor is None:
        raise HTTPException(status_code=503, detail="Model not loaded")

    try:
        # Convert candles to dict list
        candles_dict = [candle.dict(by_alias=False) for candle in request.candles]

        # Fill missing ema50 with close price if not provided
        for candle in candles_dict:
            if candle['ema50'] is None:
                candle['ema50'] = candle['close']

        # Prepare features
        df = prepare_data_from_candles(candles_dict, preprocessor)

        # Check if we have enough data
        if len(df) < preprocessor.lookback:
            raise HTTPException(
                status_code=400,
                detail=f"Insufficient data: need at least {preprocessor.lookback} candles, got {len(df)}"
            )

        # Get last sequence
        feature_cols = preprocessor.feature_columns
        sequence = df[feature_cols].iloc[-preprocessor.lookback:].values

        # Scale
        sequence_scaled = preprocessor.scaler.transform(sequence)

        # Convert to tensor
        X = torch.FloatTensor(sequence_scaled).unsqueeze(0).to(device)  # (1, lookback, features)

        # Inference
        with torch.no_grad():
            if hasattr(model, 'attention'):  # TransformerLSTM
                class_logits, reg_pred, attn_weights = model(X)

                # Process attention weights (average across heads and get last timestep importance)
                attn_mean = attn_weights.mean(dim=1)[0, -1, :].cpu().numpy()  # (seq_len,)

                # Find top 5 most important timesteps
                top_indices = np.argsort(attn_mean)[-5:]
                attention_summary = {
                    f"t-{preprocessor.lookback - idx}": float(attn_mean[idx])
                    for idx in top_indices
                }
            else:  # LightweightLSTM
                class_logits, reg_pred = model(X)
                attention_summary = None

        # Get predictions
        probs = torch.softmax(class_logits, dim=1)[0].cpu().numpy()
        pred_class = int(probs.argmax())
        confidence = float(probs[pred_class])
        expected_return = float(reg_pred[0, 0].cpu())

        # Map to labels
        label_map = {0: 'DOWN', 1: 'SIDEWAYS', 2: 'UP'}
        prediction = label_map[pred_class]

        # Calculate simple trend score (for reference)
        try:
            current = df.iloc[-1]
            previous = df.iloc[-2]

            trend_score = 0
            if current['close'] > current['ema50']:
                trend_score += 1
            if current['macd'] > current['macd_signal']:
                trend_score += 1
            if current['macd_hist'] > previous['macd_hist']:
                trend_score += 1
            if current['rsi'] > 50:
                trend_score += 1
            if current['rsi'] > previous['rsi']:
                trend_score += 1

            # Bearish signals
            if current['close'] < current['ema50']:
                trend_score -= 1
            if current['macd'] < current['macd_signal']:
                trend_score -= 1
            if current['macd_hist'] < previous['macd_hist']:
                trend_score -= 1
            if current['rsi'] < 50:
                trend_score -= 1
            if current['rsi'] < previous['rsi']:
                trend_score -= 1
        except Exception as e:
            logger.warning(f"Could not calculate trend score: {e}")
            trend_score = None

        return PredictionResponse(
            symbol=request.symbol,
            prediction=prediction,
            confidence=confidence,
            probabilities={
                "down": float(probs[0]),
                "sideways": float(probs[1]),
                "up": float(probs[2])
            },
            expected_return=expected_return,
            trend_score=trend_score,
            attention_summary=attention_summary
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Prediction error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Prediction failed: {str(e)}")


@app.get("/model/info")
async def model_info():
    """Get model information"""
    if model is None or preprocessor is None:
        raise HTTPException(status_code=503, detail="Model not loaded")

    return {
        "model_type": type(model).__name__,
        "input_features": len(preprocessor.feature_columns),
        "feature_names": preprocessor.feature_columns,
        "lookback": preprocessor.lookback,
        "forward_bars": preprocessor.forward_bars,
        "threshold": preprocessor.threshold,
        "device": str(device)
    }


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(
        "prediction_service:app",
        host="0.0.0.0",
        port=8000,
        reload=False,  # Set to True for development
        log_level="info"
    )

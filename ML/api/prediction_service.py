"""
FastAPI service for real-time trading predictions
Provides HTTP endpoint for C# application to get ML predictions
"""

from contextlib import asynccontextmanager
from pathlib import Path
import logging
import sys
from typing import List, Dict, Optional
import os

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field, ConfigDict
import numpy as np

# Ensure compatibility with PyTorch's NumPy expectations
if not hasattr(np, "_ARRAY_API"):
    np._ARRAY_API = None

import torch
from openai import OpenAI
from dotenv import load_dotenv

# Load environment variables
load_dotenv(Path(__file__).parent.parent / '.env')

# Add parent directory to path
sys.path.append(str(Path(__file__).parent.parent))

from trading_model.models.transformer_lstm import create_model
from trading_model.utils.preprocessor import TradingDataPreprocessor, prepare_data_from_candles

# Setup logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Global model and preprocessor (loaded on startup)
model = None
preprocessor = None
device = None

# OpenAI client
openai_client = None
openai_model = None


class CandleData(BaseModel):
    """Single candle data point"""

    model_config = ConfigDict(populate_by_name=True)  # Allow both 'macd_signal' and 'macdSign'

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


class PredictionRequest(BaseModel):
    """Request format for predictions"""
    symbol: str
    candles: List[CandleData] = Field(..., min_length=50, description="Minimum 50 candles required")


class PredictionResponse(BaseModel):
    """Response format for predictions"""
    symbol: str
    prediction: str  # "UP", "DOWN", or "SIDEWAYS"
    confidence: float  # 0.0 to 1.0
    probabilities: Dict[str, float]  # {"down": 0.1, "sideways": 0.2, "up": 0.7}
    expected_return: float  # Predicted return percentage
    trend_score: Optional[int] = None  # Optional: calculated trend score
    attention_summary: Optional[Dict[str, float]] = None  # Which timeframes were most important


class OpenAIIndicatorRequest(BaseModel):
    """Request format for OpenAI-based analysis"""
    symbol: str
    indicators: Dict[str, float] = Field(..., description="Technical indicators: rsi, macd, macd_signal, macd_hist, ema50, close, volume, stoch_k, stoch_d")
    previous_indicators: Optional[Dict[str, float]] = Field(None, description="Previous candle indicators for trend analysis")


class OpenAIAnalysisResponse(BaseModel):
    """Response format for OpenAI analysis"""
    symbol: str
    signal: str  # "BUY", "SELL", or "HOLD"
    confidence: float  # 0.0 to 1.0
    trading_score: int  # -10 to +10 score
    reasoning: str  # LLM explanation
    risk_level: str  # "LOW", "MEDIUM", "HIGH"
    key_factors: List[str]  # Important factors in the decision


async def load_model():
    """Load model and preprocessor on startup"""
    global model, preprocessor, device, openai_client, openai_model

    logger.info("Loading model and preprocessor...")

    # Initialize OpenAI client
    try:
        api_key = os.getenv('OPENAI_API_KEY')
        openai_model = os.getenv('OPENAI_MODEL', 'gpt-4')
        base_url = os.getenv('OPENAI_BASE_URL', 'https://api.openai.com/v1')

        if api_key:
            openai_client = OpenAI(api_key=api_key, base_url=base_url)
            logger.info(f"OpenAI client initialized with model: {openai_model}")
        else:
            logger.warning("No OPENAI_API_KEY found in environment, OpenAI features disabled")
    except Exception as e:
        logger.warning(f"Failed to initialize OpenAI client: {e}")

    try:
        # Set device
        device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
        logger.info(f"Using device: {device}")

        # Load preprocessor (relative to script's parent directory)
        base_path = Path(__file__).parent.parent
        preprocessor_path = base_path / 'checkpoints' / 'preprocessor.pkl'
        if not preprocessor_path.exists():
            raise FileNotFoundError(f"Preprocessor not found at {preprocessor_path}")

        preprocessor = TradingDataPreprocessor.load(str(preprocessor_path))
        logger.info(f"Loaded preprocessor with {len(preprocessor.feature_columns)} features")

        # Load model checkpoint
        checkpoint_path = base_path / 'checkpoints' / 'best_model.pt'
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

        logger.info("Model loaded successfully!")
        logger.info(f"  Validation accuracy: {checkpoint.get('val_accuracy', 'N/A')}")
        logger.info(f"  Validation F1: {checkpoint.get('val_f1', 'N/A')}")

    except Exception as e:
        logger.error(f"Failed to load model: {e}")
        raise


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Lifespan handler to load model on startup without deprecated events."""
    await load_model()
    yield


# Initialize FastAPI app
app = FastAPI(
    title="Trading ML Prediction Service",
    description="LSTM/Transformer model for cryptocurrency trading predictions",
    version="1.0.0",
    lifespan=lifespan
)


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

                # Process attention weights
                # attn_weights shape: (batch, seq_len, seq_len) from MultiheadAttention
                # Get the attention pattern for the last timestep (what it attends to)
                if len(attn_weights.shape) == 3:  # (batch, seq, seq)
                    attn_mean = attn_weights[0, -1, :].cpu().numpy()  # (seq_len,)
                elif len(attn_weights.shape) == 4:  # (batch, heads, seq, seq)
                    attn_mean = attn_weights.mean(dim=1)[0, -1, :].cpu().numpy()  # (seq_len,)
                else:
                    logger.warning(f"Unexpected attention weight shape: {attn_weights.shape}")
                    attn_mean = None

                # Find top 5 most important timesteps
                if attn_mean is not None:
                    top_indices = np.argsort(attn_mean)[-5:]
                    attention_summary = {
                        f"t-{preprocessor.lookback - idx}": float(attn_mean[idx])
                        for idx in top_indices
                    }
                else:
                    attention_summary = None
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


@app.post("/predict/openai", response_model=OpenAIAnalysisResponse)
async def predict_with_openai(request: OpenAIIndicatorRequest):
    """
    Analyze technical indicators using OpenAI GPT model

    Args:
        request: OpenAIIndicatorRequest with symbol and technical indicators

    Returns:
        OpenAIAnalysisResponse with trading signal, score, and reasoning
    """
    if openai_client is None:
        raise HTTPException(status_code=503, detail="OpenAI client not initialized")

    try:
        # Prepare the indicators summary
        ind = request.indicators
        prev = request.previous_indicators or {}

        # Build context for GPT
        context = f"""Analyze the following technical indicators for {request.symbol} and provide a trading recommendation.

Current Indicators:
- Price: ${ind.get('close', 0):.2f}
- RSI: {ind.get('rsi', 50):.2f} (Relative Strength Index)
- MACD: {ind.get('macd', 0):.4f}
- MACD Signal: {ind.get('macd_signal', 0):.4f}
- MACD Histogram: {ind.get('macd_hist', 0):.4f}
- EMA 50: ${ind.get('ema50', 0):.2f}
- Volume: {ind.get('volume', 0):.0f}
- Stochastic K: {ind.get('stoch_k', 50):.2f}
- Stochastic D: {ind.get('stoch_d', 50):.2f}
"""

        if prev:
            context += f"""
Previous Candle (for trend analysis):
- Price: ${prev.get('close', 0):.2f}
- RSI: {prev.get('rsi', 50):.2f}
- MACD Histogram: {prev.get('macd_hist', 0):.4f}
"""

        context += """
Please analyze these indicators and provide:
1. A clear trading signal: BUY, SELL, or HOLD
2. A confidence level (0.0 to 1.0)
3. A trading score from -10 (strong sell) to +10 (strong buy)
4. Risk level: LOW, MEDIUM, or HIGH
5. Key factors influencing your decision (list 3-5 points)
6. Brief reasoning for your recommendation

Respond in JSON format:
{
  "signal": "BUY/SELL/HOLD",
  "confidence": 0.75,
  "trading_score": 7,
  "risk_level": "MEDIUM",
  "key_factors": ["factor1", "factor2", "factor3"],
  "reasoning": "Your analysis here"
}
"""

        # Call OpenAI API
        response = openai_client.chat.completions.create(
            model=openai_model,
            messages=[
                {"role": "system", "content": "You are an expert cryptocurrency trading analyst. Provide objective, data-driven analysis based on technical indicators. Always respond in valid JSON format."},
                {"role": "user", "content": context}
            ],
            temperature=0.3,  # Lower temperature for more consistent analysis
            max_tokens=500
        )

        # Parse response
        import json
        result_text = response.choices[0].message.content.strip()

        # Remove markdown code blocks if present
        if result_text.startswith("```"):
            result_text = result_text.split("```")[1]
            if result_text.startswith("json"):
                result_text = result_text[4:]
            result_text = result_text.strip()

        result = json.loads(result_text)

        # Validate and return
        return OpenAIAnalysisResponse(
            symbol=request.symbol,
            signal=result.get('signal', 'HOLD').upper(),
            confidence=float(result.get('confidence', 0.5)),
            trading_score=int(result.get('trading_score', 0)),
            reasoning=result.get('reasoning', 'No reasoning provided'),
            risk_level=result.get('risk_level', 'MEDIUM').upper(),
            key_factors=result.get('key_factors', [])
        )

    except json.JSONDecodeError as e:
        logger.error(f"Failed to parse OpenAI response: {e}")
        logger.error(f"Response was: {result_text}")
        raise HTTPException(status_code=500, detail=f"Failed to parse AI response: {str(e)}")
    except Exception as e:
        logger.error(f"OpenAI prediction error: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"OpenAI prediction failed: {str(e)}")


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
        "device": str(device),
        "openai_enabled": openai_client is not None
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

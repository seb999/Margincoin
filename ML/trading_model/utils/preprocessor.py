"""
Data preprocessing for LSTM/Transformer trading model
Converts raw candle data into features suitable for time-series prediction
"""

import numpy as np
import pandas as pd
from sklearn.preprocessing import StandardScaler
from typing import List, Tuple, Dict
import joblib


class TradingDataPreprocessor:
    """
    Preprocesses trading data for LSTM/Transformer models

    Features created:
    - Price features (OHLCV, returns, ratios)
    - Technical indicators (RSI, MACD, EMA)
    - Pattern features (candle patterns, shadows)
    - Volatility features
    """

    def __init__(self, lookback: int = 50, forward_bars: int = 5, threshold: float = 0.002):
        """
        Args:
            lookback: Number of historical bars to use as input
            forward_bars: Number of bars ahead to predict
            threshold: Percentage threshold for UP/DOWN classification
        """
        self.lookback = lookback
        self.forward_bars = forward_bars
        self.threshold = threshold
        self.scaler = StandardScaler()
        self.feature_columns = []

    def create_features(self, df: pd.DataFrame) -> pd.DataFrame:
        """
        Enhanced feature engineering from OHLCV and indicator data

        Args:
            df: DataFrame with columns [open, high, low, close, volume, rsi, macd, macd_signal, macd_hist, ema50, stoch_k, stoch_d]

        Returns:
            DataFrame with additional engineered features
        """
        df = df.copy()

        # Price-based features
        df['returns'] = df['close'].pct_change()
        df['log_returns'] = np.log(df['close'] / df['close'].shift(1))
        df['hl_ratio'] = (df['high'] - df['low']) / df['close']
        df['co_ratio'] = (df['close'] - df['open']) / df['open'].replace(0, np.nan)

        # Volatility
        df['volatility_20'] = df['returns'].rolling(20).std()
        df['volatility_5'] = df['returns'].rolling(5).std()

        # Trend strength
        df['ema_distance'] = (df['close'] - df['ema50']) / df['ema50'].replace(0, np.nan)
        df['ema_slope'] = df['ema50'].pct_change(5)

        # MACD enhancements
        df['macd_acceleration'] = df['macd_hist'].diff()
        df['macd_hist_slope'] = df['macd_hist'].rolling(3).apply(
            lambda x: np.polyfit(range(len(x)), x, 1)[0] if len(x) == 3 else 0,
            raw=False
        )

        # RSI momentum
        df['rsi_momentum'] = df['rsi'].diff()
        df['rsi_ma'] = df['rsi'].rolling(10).mean()
        df['rsi_distance_50'] = df['rsi'] - 50

        # Volume features (if volume exists and is not zero)
        if 'volume' in df.columns and df['volume'].sum() > 0:
            df['volume_ma'] = df['volume'].rolling(20).mean()
            df['volume_ratio'] = df['volume'] / df['volume_ma'].replace(0, np.nan)
            df['volume_volatility'] = df['volume'].rolling(10).std()
        else:
            df['volume_ma'] = 0
            df['volume_ratio'] = 1
            df['volume_volatility'] = 0

        # Stochastic
        if 'stoch_k' in df.columns and 'stoch_d' in df.columns:
            df['stoch_position'] = (df['stoch_k'] + df['stoch_d']) / 2
            df['stoch_divergence'] = df['stoch_k'] - df['stoch_d']
        else:
            df['stoch_position'] = 50
            df['stoch_divergence'] = 0

        # Pattern features - candle body and shadows
        df['body_size'] = abs(df['close'] - df['open']) / df['close'].replace(0, np.nan)
        df['upper_shadow'] = (df['high'] - df[['close', 'open']].max(axis=1)) / df['close'].replace(0, np.nan)
        df['lower_shadow'] = (df[['close', 'open']].min(axis=1) - df['low']) / df['close'].replace(0, np.nan)

        # Rolling statistics - price position in range
        df['price_position'] = (
            (df['close'] - df['close'].rolling(20).min()) /
            (df['close'].rolling(20).max() - df['close'].rolling(20).min()).replace(0, np.nan)
        )

        # Momentum features
        df['momentum_5'] = df['close'].pct_change(5)
        df['momentum_10'] = df['close'].pct_change(10)

        # Replace inf values with NaN, then fill
        df = df.replace([np.inf, -np.inf], np.nan)
        df = df.ffill().fillna(0)

        return df

    def create_labels(self, df: pd.DataFrame) -> pd.DataFrame:
        """
        Create labels based on future returns

        Classification (3-class):
            - UP (2): if future return > threshold
            - DOWN (0): if future return < -threshold
            - SIDEWAYS (1): otherwise

        Regression:
            - Actual % return over next N bars

        Args:
            df: DataFrame with price data

        Returns:
            DataFrame with label columns added
        """
        df = df.copy()

        # Calculate forward returns
        df['forward_return'] = (df['close'].shift(-self.forward_bars) / df['close']) - 1

        # Classification labels
        df['label'] = 'SIDEWAYS'
        df.loc[df['forward_return'] > self.threshold, 'label'] = 'UP'
        df.loc[df['forward_return'] < -self.threshold, 'label'] = 'DOWN'

        # Numeric encoding
        label_map = {'DOWN': 0, 'SIDEWAYS': 1, 'UP': 2}
        df['label_encoded'] = df['label'].map(label_map)

        return df

    def get_feature_columns(self) -> List[str]:
        """
        Returns the list of feature columns to use for model input
        Excludes labels and metadata
        """
        # Core OHLCV
        base_features = ['open', 'high', 'low', 'close', 'volume']

        # Technical indicators (as provided by C#)
        indicator_features = ['rsi', 'macd', 'macd_signal', 'macd_hist', 'ema50',
                            'stoch_k', 'stoch_d']

        # Engineered features
        engineered_features = [
            'returns', 'log_returns', 'hl_ratio', 'co_ratio',
            'volatility_20', 'volatility_5',
            'ema_distance', 'ema_slope',
            'macd_acceleration', 'macd_hist_slope',
            'rsi_momentum', 'rsi_ma', 'rsi_distance_50',
            'volume_ma', 'volume_ratio', 'volume_volatility',
            'stoch_position', 'stoch_divergence',
            'body_size', 'upper_shadow', 'lower_shadow',
            'price_position', 'momentum_5', 'momentum_10'
        ]

        return base_features + indicator_features + engineered_features

    def create_sequences(self, df: pd.DataFrame, feature_columns: List[str] = None) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
        """
        Create sliding window sequences for LSTM/Transformer

        Args:
            df: DataFrame with features and labels
            feature_columns: List of columns to use as features (None = auto-detect)

        Returns:
            X: shape (samples, lookback, features) - input sequences
            y_class: shape (samples,) - classification labels
            y_reg: shape (samples,) - regression targets (forward returns)
        """
        if feature_columns is None:
            feature_columns = self.get_feature_columns()

        self.feature_columns = feature_columns

        # Ensure all feature columns exist
        missing_cols = [col for col in feature_columns if col not in df.columns]
        if missing_cols:
            raise ValueError(f"Missing columns in DataFrame: {missing_cols}")

        X, y_class, y_reg = [], [], []

        # Create sequences with sliding window
        for i in range(self.lookback, len(df) - self.forward_bars):
            # Get sequence of features
            sequence = df[feature_columns].iloc[i - self.lookback:i].values

            # Verify sequence shape
            if sequence.shape[0] != self.lookback:
                continue

            X.append(sequence)

            # Get labels from current timestep
            y_class.append(df['label_encoded'].iloc[i])
            y_reg.append(df['forward_return'].iloc[i])

        return np.array(X, dtype=np.float32), np.array(y_class, dtype=np.int64), np.array(y_reg, dtype=np.float32)

    def fit_scaler(self, X: np.ndarray) -> None:
        """
        Fit the scaler on training data

        Args:
            X: Training sequences of shape (samples, lookback, features)
        """
        # Reshape to 2D for scaler
        n_samples, n_timesteps, n_features = X.shape
        X_reshaped = X.reshape(-1, n_features)

        self.scaler.fit(X_reshaped)

    def transform(self, X: np.ndarray) -> np.ndarray:
        """
        Scale the features using fitted scaler

        Args:
            X: Sequences of shape (samples, lookback, features)

        Returns:
            Scaled sequences of same shape
        """
        n_samples, n_timesteps, n_features = X.shape
        X_reshaped = X.reshape(-1, n_features)

        X_scaled = self.scaler.transform(X_reshaped)

        return X_scaled.reshape(n_samples, n_timesteps, n_features)

    def save(self, path: str) -> None:
        """Save preprocessor state"""
        joblib.dump({
            'scaler': self.scaler,
            'feature_columns': self.feature_columns,
            'lookback': self.lookback,
            'forward_bars': self.forward_bars,
            'threshold': self.threshold
        }, path)

    @classmethod
    def load(cls, path: str) -> 'TradingDataPreprocessor':
        """Load preprocessor state"""
        state = joblib.load(path)
        preprocessor = cls(
            lookback=state['lookback'],
            forward_bars=state['forward_bars'],
            threshold=state['threshold']
        )
        preprocessor.scaler = state['scaler']
        preprocessor.feature_columns = state['feature_columns']
        return preprocessor


def prepare_data_from_candles(candles: List[Dict], preprocessor: TradingDataPreprocessor = None) -> pd.DataFrame:
    """
    Convert list of candle dictionaries to DataFrame with features

    Args:
        candles: List of dicts with keys: open, high, low, close, volume, rsi, macd, etc.
        preprocessor: Optional preprocessor instance (creates new one if None)

    Returns:
        DataFrame ready for sequence creation
    """
    if preprocessor is None:
        preprocessor = TradingDataPreprocessor()

    # Convert to DataFrame
    df = pd.DataFrame(candles)

    # Ensure required columns exist
    required = ['open', 'high', 'low', 'close']
    if not all(col in df.columns for col in required):
        raise ValueError(f"Missing required columns: {required}")

    # Add default values for missing indicators
    defaults = {
        'volume': 0,
        'rsi': 50,
        'macd': 0,
        'macd_signal': 0,
        'macd_hist': 0,
        'ema50': df['close'].iloc[0] if len(df) > 0 else 0,
        'stoch_k': 50,
        'stoch_d': 50
    }

    for col, default_val in defaults.items():
        if col not in df.columns:
            df[col] = default_val

    # Create features
    df = preprocessor.create_features(df)

    return df

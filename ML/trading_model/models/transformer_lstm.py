"""
Hybrid Transformer-LSTM model for trading predictions
Combines transformer attention with LSTM sequential processing
"""

import torch
import torch.nn as nn
import torch.nn.functional as F
from typing import Tuple


class TransformerLSTMModel(nn.Module):
    """
    Hybrid architecture combining Transformer and LSTM

    Architecture:
    1. Input projection
    2. Transformer encoder (captures long-range dependencies)
    3. Bidirectional LSTM (sequential processing)
    4. Multi-head attention (focuses on important timesteps)
    5. Dual heads (classification + regression)
    """

    def __init__(
        self,
        input_size: int,
        hidden_size: int = 128,
        num_lstm_layers: int = 2,
        num_transformer_layers: int = 2,
        num_heads: int = 4,
        dropout: float = 0.2,
        num_classes: int = 3
    ):
        """
        Args:
            input_size: Number of input features
            hidden_size: Size of hidden layers
            num_lstm_layers: Number of LSTM layers
            num_transformer_layers: Number of transformer encoder layers
            num_heads: Number of attention heads
            dropout: Dropout probability
            num_classes: Number of output classes (3: UP/DOWN/SIDEWAYS)
        """
        super().__init__()

        self.input_size = input_size
        self.hidden_size = hidden_size

        # Input projection layer
        self.input_proj = nn.Linear(input_size, hidden_size)
        self.input_norm = nn.LayerNorm(hidden_size)

        # Positional encoding for transformer
        self.positional_encoding = PositionalEncoding(hidden_size, dropout)

        # Transformer encoder
        encoder_layer = nn.TransformerEncoderLayer(
            d_model=hidden_size,
            nhead=num_heads,
            dim_feedforward=hidden_size * 4,
            dropout=dropout,
            batch_first=True,
            activation='gelu'
        )
        self.transformer = nn.TransformerEncoder(
            encoder_layer,
            num_layers=num_transformer_layers
        )

        # Bidirectional LSTM
        self.lstm = nn.LSTM(
            input_size=hidden_size,
            hidden_size=hidden_size,
            num_layers=num_lstm_layers,
            dropout=dropout if num_lstm_layers > 1 else 0,
            batch_first=True,
            bidirectional=True
        )

        # Self-attention to weight important timesteps
        self.attention = nn.MultiheadAttention(
            embed_dim=hidden_size * 2,  # *2 for bidirectional
            num_heads=num_heads,
            dropout=dropout,
            batch_first=True
        )

        # Classification head
        self.fc_class = nn.Sequential(
            nn.Linear(hidden_size * 2, hidden_size),
            nn.LayerNorm(hidden_size),
            nn.GELU(),
            nn.Dropout(dropout),
            nn.Linear(hidden_size, hidden_size // 2),
            nn.GELU(),
            nn.Dropout(dropout),
            nn.Linear(hidden_size // 2, num_classes)
        )

        # Regression head (predicted return %)
        self.fc_reg = nn.Sequential(
            nn.Linear(hidden_size * 2, hidden_size),
            nn.LayerNorm(hidden_size),
            nn.GELU(),
            nn.Dropout(dropout),
            nn.Linear(hidden_size, hidden_size // 2),
            nn.GELU(),
            nn.Dropout(dropout),
            nn.Linear(hidden_size // 2, 1)
        )

    def forward(self, x: torch.Tensor) -> Tuple[torch.Tensor, torch.Tensor, torch.Tensor]:
        """
        Forward pass

        Args:
            x: Input tensor of shape (batch, sequence_length, input_size)

        Returns:
            class_logits: (batch, num_classes) - classification predictions
            regression: (batch, 1) - predicted returns
            attention_weights: (batch, sequence_length, sequence_length) - attention map
        """
        batch_size, seq_len, _ = x.shape

        # Project input
        x = self.input_proj(x)  # (batch, seq, hidden)
        x = self.input_norm(x)

        # Add positional encoding
        x = self.positional_encoding(x)

        # Transformer encoding
        x = self.transformer(x)  # (batch, seq, hidden)

        # LSTM processing
        lstm_out, _ = self.lstm(x)  # (batch, seq, hidden*2)

        # Self-attention to focus on important timesteps
        attn_out, attn_weights = self.attention(
            lstm_out, lstm_out, lstm_out
        )  # (batch, seq, hidden*2), (batch, seq, seq)

        # Use last timestep for prediction
        final_hidden = attn_out[:, -1, :]  # (batch, hidden*2)

        # Dual outputs
        class_logits = self.fc_class(final_hidden)  # (batch, num_classes)
        regression = self.fc_reg(final_hidden)      # (batch, 1)

        return class_logits, regression, attn_weights


class LightweightLSTM(nn.Module):
    """
    Lightweight LSTM model for faster inference
    Suitable for production deployment where latency matters
    """

    def __init__(
        self,
        input_size: int,
        hidden_size: int = 64,
        num_layers: int = 2,
        dropout: float = 0.2,
        num_classes: int = 3
    ):
        super().__init__()

        self.lstm = nn.LSTM(
            input_size=input_size,
            hidden_size=hidden_size,
            num_layers=num_layers,
            dropout=dropout if num_layers > 1 else 0,
            batch_first=True,
            bidirectional=True
        )

        self.fc = nn.Sequential(
            nn.Linear(hidden_size * 2, hidden_size),
            nn.ReLU(),
            nn.Dropout(dropout),
            nn.Linear(hidden_size, num_classes)
        )

        self.fc_reg = nn.Sequential(
            nn.Linear(hidden_size * 2, hidden_size),
            nn.ReLU(),
            nn.Dropout(dropout),
            nn.Linear(hidden_size, 1)
        )

    def forward(self, x: torch.Tensor) -> Tuple[torch.Tensor, torch.Tensor]:
        """
        Forward pass

        Args:
            x: (batch, sequence_length, input_size)

        Returns:
            class_logits: (batch, num_classes)
            regression: (batch, 1)
        """
        lstm_out, _ = self.lstm(x)  # (batch, seq, hidden*2)
        final_hidden = lstm_out[:, -1, :]  # Last timestep

        class_logits = self.fc(final_hidden)
        regression = self.fc_reg(final_hidden)

        return class_logits, regression


class PositionalEncoding(nn.Module):
    """
    Positional encoding for transformer
    Adds position information to the input sequences
    """

    def __init__(self, d_model: int, dropout: float = 0.1, max_len: int = 5000):
        super().__init__()
        self.dropout = nn.Dropout(p=dropout)

        position = torch.arange(max_len).unsqueeze(1)
        div_term = torch.exp(torch.arange(0, d_model, 2) * (-torch.log(torch.tensor(10000.0)) / d_model))

        pe = torch.zeros(1, max_len, d_model)
        pe[0, :, 0::2] = torch.sin(position * div_term)
        pe[0, :, 1::2] = torch.cos(position * div_term)

        self.register_buffer('pe', pe)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        """
        Args:
            x: Tensor of shape (batch, seq_len, d_model)
        """
        x = x + self.pe[:, :x.size(1), :]
        return self.dropout(x)


def create_model(model_type: str, input_size: int, **kwargs) -> nn.Module:
    """
    Factory function to create models

    Args:
        model_type: 'transformer_lstm' or 'lightweight_lstm'
        input_size: Number of input features
        **kwargs: Additional arguments for model constructor

    Returns:
        PyTorch model
    """
    if model_type == 'transformer_lstm':
        model = TransformerLSTMModel(input_size=input_size, **kwargs)
    elif model_type == 'lightweight_lstm':
        model = LightweightLSTM(input_size=input_size, **kwargs)
    else:
        raise ValueError(f"Unknown model type: {model_type}")

    # Attach metadata so checkpoints carry architecture info
    model.model_type = model_type
    model.model_kwargs = kwargs
    model.model_input_size = input_size
    return model

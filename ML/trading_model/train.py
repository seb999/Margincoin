"""
Training pipeline for LSTM/Transformer trading model
"""

import torch
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import Dataset, DataLoader, random_split
import numpy as np
import pandas as pd
from pathlib import Path
from typing import Tuple, Dict
import json
from datetime import datetime

from models.transformer_lstm import create_model
from utils.preprocessor import TradingDataPreprocessor


class TradingDataset(Dataset):
    """PyTorch Dataset for trading sequences"""

    def __init__(self, X: np.ndarray, y_class: np.ndarray, y_reg: np.ndarray):
        self.X = torch.FloatTensor(X)
        self.y_class = torch.LongTensor(y_class)
        self.y_reg = torch.FloatTensor(y_reg).unsqueeze(1)

    def __len__(self) -> int:
        return len(self.X)

    def __getitem__(self, idx: int) -> Tuple[torch.Tensor, torch.Tensor, torch.Tensor]:
        return self.X[idx], self.y_class[idx], self.y_reg[idx]


class TradingModelTrainer:
    """
    Handles model training, validation, and checkpointing
    """

    def __init__(
        self,
        model: nn.Module,
        device: str = 'cuda' if torch.cuda.is_available() else 'cpu',
        learning_rate: float = 0.001,
        weight_decay: float = 1e-5
    ):
        self.model = model.to(device)
        self.device = device

        self.optimizer = optim.AdamW(
            model.parameters(),
            lr=learning_rate,
            weight_decay=weight_decay
        )

        self.scheduler = optim.lr_scheduler.ReduceLROnPlateau(
            self.optimizer,
            mode='max',
            patience=5,
            factor=0.5,
            verbose=True
        )

        # Loss functions
        self.criterion_class = nn.CrossEntropyLoss()
        self.criterion_reg = nn.MSELoss()

        # Training history
        self.history = {
            'train_loss': [],
            'val_loss': [],
            'val_accuracy': [],
            'val_f1': [],
            'learning_rate': []
        }

    def train_epoch(self, train_loader: DataLoader) -> float:
        """Train for one epoch"""
        self.model.train()
        total_loss = 0
        n_batches = 0

        for X_batch, y_class_batch, y_reg_batch in train_loader:
            X_batch = X_batch.to(self.device)
            y_class_batch = y_class_batch.to(self.device)
            y_reg_batch = y_reg_batch.to(self.device)

            self.optimizer.zero_grad()

            # Forward pass
            if hasattr(self.model, 'attention'):  # TransformerLSTM
                class_logits, reg_pred, _ = self.model(X_batch)
            else:  # LightweightLSTM
                class_logits, reg_pred = self.model(X_batch)

            # Multi-task loss
            loss_class = self.criterion_class(class_logits, y_class_batch)
            loss_reg = self.criterion_reg(reg_pred, y_reg_batch)

            # Combined loss (classification weighted higher)
            loss = loss_class + 0.3 * loss_reg

            loss.backward()

            # Gradient clipping
            torch.nn.utils.clip_grad_norm_(self.model.parameters(), max_norm=1.0)

            self.optimizer.step()

            total_loss += loss.item()
            n_batches += 1

        return total_loss / n_batches

    @torch.no_grad()
    def validate(self, val_loader: DataLoader) -> Dict[str, float]:
        """Validate model"""
        self.model.eval()

        total_loss = 0
        all_preds = []
        all_labels = []
        n_batches = 0

        for X_batch, y_class_batch, y_reg_batch in val_loader:
            X_batch = X_batch.to(self.device)
            y_class_batch = y_class_batch.to(self.device)
            y_reg_batch = y_reg_batch.to(self.device)

            # Forward pass
            if hasattr(self.model, 'attention'):
                class_logits, reg_pred, _ = self.model(X_batch)
            else:
                class_logits, reg_pred = self.model(X_batch)

            # Loss
            loss_class = self.criterion_class(class_logits, y_class_batch)
            loss_reg = self.criterion_reg(reg_pred, y_reg_batch)
            loss = loss_class + 0.3 * loss_reg

            total_loss += loss.item()
            n_batches += 1

            # Predictions
            pred_class = class_logits.argmax(dim=1)
            all_preds.extend(pred_class.cpu().numpy())
            all_labels.extend(y_class_batch.cpu().numpy())

        # Calculate metrics
        all_preds = np.array(all_preds)
        all_labels = np.array(all_labels)

        accuracy = (all_preds == all_labels).mean()

        # Per-class accuracy
        class_accuracies = {}
        for cls in [0, 1, 2]:  # DOWN, SIDEWAYS, UP
            mask = all_labels == cls
            if mask.sum() > 0:
                class_accuracies[cls] = (all_preds[mask] == all_labels[mask]).mean()

        # F1-score (macro average)
        f1_scores = []
        for cls in [0, 1, 2]:
            tp = ((all_preds == cls) & (all_labels == cls)).sum()
            fp = ((all_preds == cls) & (all_labels != cls)).sum()
            fn = ((all_preds != cls) & (all_labels == cls)).sum()

            precision = tp / (tp + fp) if (tp + fp) > 0 else 0
            recall = tp / (tp + fn) if (tp + fn) > 0 else 0
            f1 = 2 * precision * recall / (precision + recall) if (precision + recall) > 0 else 0
            f1_scores.append(f1)

        macro_f1 = np.mean(f1_scores)

        return {
            'loss': total_loss / n_batches,
            'accuracy': accuracy,
            'f1': macro_f1,
            'class_accuracies': class_accuracies
        }

    def train(
        self,
        train_loader: DataLoader,
        val_loader: DataLoader,
        epochs: int = 100,
        save_dir: str = 'checkpoints',
        early_stopping_patience: int = 15
    ) -> Dict:
        """
        Full training loop with validation and checkpointing

        Args:
            train_loader: Training data loader
            val_loader: Validation data loader
            epochs: Number of epochs
            save_dir: Directory to save checkpoints
            early_stopping_patience: Stop if no improvement for N epochs

        Returns:
            Training history dictionary
        """
        save_path = Path(save_dir)
        save_path.mkdir(exist_ok=True)

        best_val_acc = 0
        best_val_f1 = 0
        patience_counter = 0

        print(f"Training on device: {self.device}")
        print(f"Model parameters: {sum(p.numel() for p in self.model.parameters()):,}")

        for epoch in range(epochs):
            # Train
            train_loss = self.train_epoch(train_loader)

            # Validate
            val_metrics = self.validate(val_loader)

            # Update scheduler
            self.scheduler.step(val_metrics['accuracy'])

            # Record history
            self.history['train_loss'].append(train_loss)
            self.history['val_loss'].append(val_metrics['loss'])
            self.history['val_accuracy'].append(val_metrics['accuracy'])
            self.history['val_f1'].append(val_metrics['f1'])
            self.history['learning_rate'].append(self.optimizer.param_groups[0]['lr'])

            # Print progress
            print(f"Epoch {epoch+1}/{epochs}")
            print(f"  Train Loss: {train_loss:.4f}")
            print(f"  Val Loss: {val_metrics['loss']:.4f}")
            print(f"  Val Accuracy: {val_metrics['accuracy']:.4f}")
            print(f"  Val F1: {val_metrics['f1']:.4f}")
            print(f"  Class Accuracies: DOWN={val_metrics['class_accuracies'].get(0, 0):.3f}, "
                  f"SIDEWAYS={val_metrics['class_accuracies'].get(1, 0):.3f}, "
                  f"UP={val_metrics['class_accuracies'].get(2, 0):.3f}")
            print(f"  LR: {self.optimizer.param_groups[0]['lr']:.6f}")

            # Save best model
            if val_metrics['accuracy'] > best_val_acc:
                best_val_acc = val_metrics['accuracy']
                best_val_f1 = val_metrics['f1']
                patience_counter = 0

                checkpoint = {
                    'epoch': epoch,
                    'model_state_dict': self.model.state_dict(),
                    'optimizer_state_dict': self.optimizer.state_dict(),
                    'val_accuracy': val_metrics['accuracy'],
                    'val_f1': val_metrics['f1'],
                    'history': self.history
                }

                torch.save(checkpoint, save_path / 'best_model.pt')
                print(f"  ✓ Saved best model (acc={val_metrics['accuracy']:.4f}, f1={val_metrics['f1']:.4f})")
            else:
                patience_counter += 1

            # Early stopping
            if patience_counter >= early_stopping_patience:
                print(f"\nEarly stopping triggered after {epoch+1} epochs")
                break

            print()

        print(f"\nTraining completed!")
        print(f"Best validation accuracy: {best_val_acc:.4f}")
        print(f"Best validation F1: {best_val_f1:.4f}")

        # Save final history
        with open(save_path / 'history.json', 'w') as f:
            json.dump(self.history, f, indent=2)

        return self.history


def prepare_dataloaders(
    csv_path: str,
    preprocessor: TradingDataPreprocessor = None,
    batch_size: int = 32,
    val_split: float = 0.2,
    max_rows: int = None,
    lookback: int = 30
) -> Tuple[DataLoader, DataLoader, TradingDataPreprocessor]:
    """
    Load data from CSV and create train/val dataloaders

    Args:
        csv_path: Path to CSV file with OHLCV and indicator data
        preprocessor: Optional preprocessor (creates new if None)
        batch_size: Batch size for dataloaders
        val_split: Validation set fraction
        max_rows: Maximum number of rows to use (None = all data)
        lookback: Number of historical bars to use as input

    Returns:
        train_loader, val_loader, preprocessor
    """
    # Load data (limit rows to save memory)
    if max_rows:
        # Read file line count first
        total_rows = sum(1 for _ in open(csv_path)) - 1  # -1 for header
        skip_rows = max(0, total_rows - max_rows)
        if skip_rows > 0:
            print(f"Loading last {max_rows} rows of {total_rows} total rows (skipping {skip_rows})")
            df = pd.read_csv(csv_path, skiprows=range(1, skip_rows + 1))
        else:
            df = pd.read_csv(csv_path)
    else:
        df = pd.read_csv(csv_path)

    if preprocessor is None:
        # Use lookback parameter (reduced to save memory)
        preprocessor = TradingDataPreprocessor(lookback=lookback, forward_bars=5, threshold=0.002)

    # Create features and labels
    df = preprocessor.create_features(df)
    df = preprocessor.create_labels(df)

    # Create sequences
    feature_cols = preprocessor.get_feature_columns()
    X, y_class, y_reg = preprocessor.create_sequences(df, feature_cols)

    print(f"Created {len(X)} sequences")
    print(f"Input shape: {X.shape}")
    print(f"Label distribution: DOWN={np.sum(y_class==0)}, SIDEWAYS={np.sum(y_class==1)}, UP={np.sum(y_class==2)}")

    # Fit scaler on training data only
    split_idx = int(len(X) * (1 - val_split))
    X_train_raw = X[:split_idx]
    X_val_raw = X[split_idx:]

    preprocessor.fit_scaler(X_train_raw)

    # Transform data
    X_train = preprocessor.transform(X_train_raw)
    X_val = preprocessor.transform(X_val_raw)

    y_class_train = y_class[:split_idx]
    y_class_val = y_class[split_idx:]

    y_reg_train = y_reg[:split_idx]
    y_reg_val = y_reg[split_idx:]

    # Create datasets
    train_dataset = TradingDataset(X_train, y_class_train, y_reg_train)
    val_dataset = TradingDataset(X_val, y_class_val, y_reg_val)

    # Create dataloaders
    train_loader = DataLoader(train_dataset, batch_size=batch_size, shuffle=True, num_workers=0)
    val_loader = DataLoader(val_dataset, batch_size=batch_size, shuffle=False, num_workers=0)

    return train_loader, val_loader, preprocessor


if __name__ == '__main__':
    # Example usage
    print("Training LSTM/Transformer Trading Model")
    print("=" * 50)

    # Configuration
    MODEL_TYPE = 'lightweight_lstm'  # Use lighter model to prevent OOM
    DATA_PATH = '../data/training_data.csv'  # Path relative to trading_model/
    SAVE_DIR = 'checkpoints'
    BATCH_SIZE = 16  # Reduced to save memory - allows using all data
    EPOCHS = 100
    LEARNING_RATE = 0.001
    LOOKBACK = 30  # Balance between context and memory

    # Prepare data - use all available data
    train_loader, val_loader, preprocessor = prepare_dataloaders(
        csv_path=DATA_PATH,
        batch_size=BATCH_SIZE,
        val_split=0.2,
        max_rows=None,  # Use all data
        lookback=LOOKBACK
    )

    # Save preprocessor
    preprocessor.save(f'{SAVE_DIR}/preprocessor.pkl')
    print(f"Saved preprocessor to {SAVE_DIR}/preprocessor.pkl")

    # Create model
    input_size = len(preprocessor.feature_columns)
    model = create_model(
        model_type=MODEL_TYPE,
        input_size=input_size,
        hidden_size=64,  # Reduced from 128 to save memory
        num_layers=2,  # For lightweight_lstm
        dropout=0.3
    )

    print(f"\nCreated {MODEL_TYPE} model with {input_size} input features")

    # Create trainer
    trainer = TradingModelTrainer(
        model=model,
        learning_rate=LEARNING_RATE
    )

    # Train
    history = trainer.train(
        train_loader=train_loader,
        val_loader=val_loader,
        epochs=EPOCHS,
        save_dir=SAVE_DIR,
        early_stopping_patience=15
    )

    print("\nTraining complete!")

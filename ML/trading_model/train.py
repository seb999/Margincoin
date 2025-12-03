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


class FocalLoss(nn.Module):
    """Focal Loss for multi-class classification to focus on hard examples"""

    def __init__(self, gamma: float = 1.5, weight: torch.Tensor = None, reduction: str = 'mean'):
        super().__init__()
        self.gamma = gamma
        self.weight = weight
        self.reduction = reduction

    def forward(self, logits: torch.Tensor, targets: torch.Tensor) -> torch.Tensor:
        log_probs = nn.functional.log_softmax(logits, dim=1)
        probs = log_probs.exp()
        focal_factor = (1 - probs) ** self.gamma
        loss = -focal_factor * log_probs

        if self.weight is not None:
            loss = loss * self.weight

        loss = loss.gather(1, targets.unsqueeze(1)).squeeze(1)

        if self.reduction == 'mean':
            return loss.mean()
        elif self.reduction == 'sum':
            return loss.sum()
        else:
            return loss


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
        weight_decay: float = 1e-5,
        use_amp: bool = True,  # Mixed precision training
        gradient_accumulation_steps: int = 1,  # Simulate larger batches
        use_focal_loss: bool = True,
        focal_gamma: float = 1.5
    ):
        self.model = model.to(device)
        self.device = device
        self.use_amp = use_amp and device == 'cuda'
        self.gradient_accumulation_steps = gradient_accumulation_steps
        self.use_focal_loss = use_focal_loss
        self.focal_gamma = focal_gamma

        self.optimizer = optim.AdamW(
            model.parameters(),
            lr=learning_rate,
            weight_decay=weight_decay
        )

        # Use val loss to drive LR reduction (more standard than accuracy)
        self.scheduler = optim.lr_scheduler.ReduceLROnPlateau(
            self.optimizer,
            mode='min',
            patience=5,
            factor=0.5
        )

        # Mixed precision scaler
        if self.use_amp:
            try:
                # Newer PyTorch API
                self.scaler = torch.amp.GradScaler('cuda')
            except TypeError:
                # Fallback for older versions
                self.scaler = torch.cuda.amp.GradScaler()
        else:
            self.scaler = None

        # Loss functions (with class weights for imbalanced data)
        self.criterion_class = None  # Will be set with weights
        self.criterion_reg = nn.MSELoss()
        self.class_weights = None

        # Training history
        self.history = {
            'train_loss': [],
            'val_loss': [],
            'val_accuracy': [],
            'val_f1': [],
            'learning_rate': []
        }

    def set_class_weights(self, train_loader: DataLoader):
        """
        Calculate class weights from training data to handle imbalance.
        Uses inverse frequency with smoothing and normalization for stability.
        """
        all_labels = []
        for _, y_class, _ in train_loader:
            all_labels.extend(y_class.cpu().numpy())

        all_labels = np.array(all_labels, dtype=np.int64)
        if all_labels.size == 0:
            raise ValueError("Training loader contains no labels")

        # Count each class (ensure we cover all 3 classes)
        class_counts = np.bincount(all_labels, minlength=3).astype(np.float64)

        # Add tiny smoothing to avoid division by zero
        eps = 1e-6
        smoothed_counts = class_counts + eps

        # Inverse frequency weighting, normalized so mean weight == 1
        inv_freq = 1.0 / smoothed_counts
        weights = inv_freq * (smoothed_counts.mean())

        # Apply sqrt to soften the weighting (prevents over-compensation)
        weights = np.sqrt(weights)

        # Mild boost to reduce bias without overcorrecting
        weights[0] *= 1.05  # DOWN
        weights[1] *= 1.15  # SIDEWAYS
        weights[2] *= 1.05  # UP

        # Convert to tensor
        self.class_weights = torch.FloatTensor(weights).to(self.device)

        # Create weighted loss (Focal or CrossEntropy)
        if self.use_focal_loss:
            self.criterion_class = FocalLoss(gamma=self.focal_gamma, weight=self.class_weights)
        else:
            self.criterion_class = nn.CrossEntropyLoss(weight=self.class_weights)

        print(f"\nClass distribution in training data (N={len(all_labels)}):")
        for i, count in enumerate(class_counts):
            class_name = ['DOWN', 'SIDEWAYS', 'UP'][i]
            pct = (count / len(all_labels)) * 100
            print(f"  {class_name:10s}: {int(count):7,} ({pct:5.2f}%) - weight: {weights[i]:.4f}")
        print()

    def train_epoch(self, train_loader: DataLoader) -> float:
        """Train for one epoch with mixed precision and gradient accumulation"""
        self.model.train()
        total_loss = 0
        n_batches = 0

        for batch_idx, (X_batch, y_class_batch, y_reg_batch) in enumerate(train_loader):
            X_batch = X_batch.to(self.device, non_blocking=True)
            y_class_batch = y_class_batch.to(self.device, non_blocking=True)
            y_reg_batch = y_reg_batch.to(self.device, non_blocking=True)

            # Mixed precision forward pass
            try:
                autocast_ctx = torch.amp.autocast('cuda', enabled=self.use_amp)
            except TypeError:
                autocast_ctx = torch.cuda.amp.autocast(enabled=self.use_amp)
            with autocast_ctx:
                # Forward pass
                if hasattr(self.model, 'attention'):  # TransformerLSTM
                    class_logits, reg_pred, _ = self.model(X_batch)
                else:  # LightweightLSTM
                    class_logits, reg_pred = self.model(X_batch)

                # Multi-task loss
                loss_class = self.criterion_class(class_logits, y_class_batch)
                loss_reg = self.criterion_reg(reg_pred, y_reg_batch)

                # Combined loss (classification weighted higher)
                loss = (loss_class + 0.3 * loss_reg) / self.gradient_accumulation_steps

            # Mixed precision backward pass
            if self.use_amp:
                self.scaler.scale(loss).backward()
            else:
                loss.backward()

            # Only update weights every N steps
            if (batch_idx + 1) % self.gradient_accumulation_steps == 0:
                if self.use_amp:
                    # Gradient clipping
                    self.scaler.unscale_(self.optimizer)
                    torch.nn.utils.clip_grad_norm_(self.model.parameters(), max_norm=1.0)
                    self.scaler.step(self.optimizer)
                    self.scaler.update()
                else:
                    torch.nn.utils.clip_grad_norm_(self.model.parameters(), max_norm=1.0)
                    self.optimizer.step()

                self.optimizer.zero_grad()

            total_loss += loss.item() * self.gradient_accumulation_steps
            n_batches += 1

        return total_loss / n_batches

    @torch.no_grad()
    def validate(self, val_loader: DataLoader) -> Dict[str, float]:
        """Validate model with mixed precision"""
        self.model.eval()

        total_loss = 0
        all_preds = []
        all_labels = []
        n_batches = 0

        for X_batch, y_class_batch, y_reg_batch in val_loader:
            X_batch = X_batch.to(self.device, non_blocking=True)
            y_class_batch = y_class_batch.to(self.device, non_blocking=True)
            y_reg_batch = y_reg_batch.to(self.device, non_blocking=True)

            # Mixed precision forward pass
            try:
                autocast_ctx = torch.amp.autocast('cuda', enabled=self.use_amp)
            except TypeError:
                autocast_ctx = torch.cuda.amp.autocast(enabled=self.use_amp)
            with autocast_ctx:
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

        accuracy = float((all_preds == all_labels).mean()) if all_labels.size > 0 else 0.0

        # Per-class accuracy (handle missing classes)
        class_accuracies = {}
        for cls in [0, 1, 2]:  # DOWN, SIDEWAYS, UP
            mask = all_labels == cls
            if mask.sum() > 0:
                class_accuracies[cls] = float((all_preds[mask] == all_labels[mask]).mean())
            else:
                class_accuracies[cls] = None  # No examples in validation set

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
        save_path.mkdir(parents=True, exist_ok=True)

        best_val_f1 = -1.0
        best_val_acc = -1.0
        patience_counter = 0

        print(f"Training on device: {self.device}")
        print(f"Mixed precision (AMP): {'Enabled' if self.use_amp else 'Disabled'}")
        print(f"Model parameters: {sum(p.numel() for p in self.model.parameters()):,}")

        # Calculate class weights to handle imbalanced data
        self.set_class_weights(train_loader)

        for epoch in range(1, epochs + 1):
            # Train
            train_loss = self.train_epoch(train_loader)

            # Validate
            val_metrics = self.validate(val_loader)

            # Update scheduler with val loss (minimization)
            self.scheduler.step(val_metrics['loss'])

            # Record history
            self.history['train_loss'].append(train_loss)
            self.history['val_loss'].append(val_metrics['loss'])
            self.history['val_accuracy'].append(val_metrics['accuracy'])
            self.history['val_f1'].append(val_metrics['f1'])
            self.history['learning_rate'].append(self.optimizer.param_groups[0]['lr'])

            # Print progress
            print(f"Epoch {epoch}/{epochs}")
            print(f"  Train Loss: {train_loss:.6f}")
            print(f"  Val Loss: {val_metrics['loss']:.6f}")
            print(f"  Val Accuracy: {val_metrics['accuracy']:.6f}")
            print(f"  Val F1: {val_metrics['f1']:.6f}")

            # Format class accuracies (handle None values)
            class_acc_parts = []
            for cls, name in enumerate(['DOWN', 'SIDEWAYS', 'UP']):
                acc = val_metrics['class_accuracies'].get(cls)
                if acc is not None:
                    class_acc_parts.append(f"{name}={acc:.3f}")
                else:
                    class_acc_parts.append(f"{name}=None")
            print(f"  Class Accuracies: {', '.join(class_acc_parts)}")
            print(f"  LR: {self.optimizer.param_groups[0]['lr']:.8f}")

            # Save best model by F1 (primary metric)
            saved = False
            if val_metrics['f1'] > best_val_f1:
                best_val_f1 = val_metrics['f1']
                best_val_acc = val_metrics['accuracy']
                patience_counter = 0

                checkpoint = {
                    'epoch': epoch,
                    'model_state_dict': self.model.state_dict(),
                    'optimizer_state_dict': self.optimizer.state_dict(),
                    'val_accuracy': val_metrics['accuracy'],
                    'val_f1': val_metrics['f1'],
                    'history': self.history,
                    'model_type': getattr(self.model, 'model_type', None),
                    'model_kwargs': getattr(self.model, 'model_kwargs', None)
                }
                torch.save(checkpoint, save_path / 'best_model.pt')
                print(f"  ✓ Saved best model by F1 (f1={val_metrics['f1']:.4f}, acc={val_metrics['accuracy']:.4f})")
                saved = True
            else:
                patience_counter += 1

            # Also save best accuracy separately (optional)
            if val_metrics['accuracy'] > best_val_acc and not saved:
                best_val_acc = val_metrics['accuracy']
                torch.save({
                    'epoch': epoch,
                    'model_state_dict': self.model.state_dict(),
                    'optimizer_state_dict': self.optimizer.state_dict(),
                    'val_accuracy': val_metrics['accuracy'],
                    'val_f1': val_metrics['f1'],
                    'history': self.history,
                    'model_type': getattr(self.model, 'model_type', None),
                    'model_kwargs': getattr(self.model, 'model_kwargs', None)
                }, save_path / 'best_model_by_acc.pt')
                print(f"  ✓ Saved best model by Accuracy (acc={val_metrics['accuracy']:.4f})")

            # Early stopping based on F1
            if patience_counter >= early_stopping_patience:
                print(f"\nEarly stopping triggered after epoch {epoch} (no F1 improvement in {early_stopping_patience} epochs)")
                break

            print()

        # Save final model & history
        torch.save({
            'model_state_dict': self.model.state_dict(),
            'model_type': getattr(self.model, 'model_type', None),
            'model_kwargs': getattr(self.model, 'model_kwargs', None),
            'history': self.history
        }, save_path / 'final_model.pt')
        with open(save_path / 'history.json', 'w') as f:
            json.dump(self.history, f, indent=2)

        print(f"\nTraining completed!")
        print(f"Best validation F1: {best_val_f1:.6f}")
        print(f"Best validation accuracy: {best_val_acc:.6f}")

        return self.history


def prepare_dataloaders(
    csv_path: str,
    preprocessor: TradingDataPreprocessor = None,
    batch_size: int = 32,
    val_split: float = 0.2,
    max_rows: int = None,
    lookback: int = 30,
    num_workers: int = 0
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
    import sys
    print("  Step 1/5: Reading CSV file...")
    sys.stdout.flush()

    if max_rows:
        # Read file line count first
        total_rows = sum(1 for _ in open(csv_path)) - 1  # -1 for header
        skip_rows = max(0, total_rows - max_rows)
        if skip_rows > 0:
            print(f"  Loading last {max_rows} rows of {total_rows} total rows (skipping {skip_rows})")
            sys.stdout.flush()
            df = pd.read_csv(csv_path, skiprows=range(1, skip_rows + 1))
        else:
            df = pd.read_csv(csv_path)
    else:
        df = pd.read_csv(csv_path)

    print(f"  ✓ Loaded {len(df)} rows")
    sys.stdout.flush()

    print("  Step 2/5: Creating preprocessor...")
    sys.stdout.flush()
    if preprocessor is None:
        preprocessor = TradingDataPreprocessor(lookback=lookback, forward_bars=5, threshold=0.002)
    print("  ✓ Preprocessor created")
    sys.stdout.flush()

    # Chronological split BEFORE labels/sequences to avoid leakage
    split_idx = int(len(df) * (1 - val_split))
    train_df_raw = df.iloc[:split_idx].copy()
    val_df_raw = df.iloc[split_idx:].copy()

    min_needed = preprocessor.lookback + preprocessor.forward_bars
    if len(val_df_raw) < min_needed:
        raise ValueError(f"Validation split too small for lookback={preprocessor.lookback} "
                         f"and forward_bars={preprocessor.forward_bars} (need >= {min_needed} rows)")

    print(f"  Train rows: {len(train_df_raw)}, Val rows: {len(val_df_raw)}")
    sys.stdout.flush()

    # Create features and labels separately per split
    print("  Step 3/5: Creating features and labels (train)...")
    sys.stdout.flush()
    train_df = preprocessor.create_features(train_df_raw)
    train_df = preprocessor.create_labels(train_df)
    print("  ✓ Train features/labels created")
    sys.stdout.flush()

    print("  Step 4/5: Creating features and labels (val)...")
    sys.stdout.flush()
    val_df = preprocessor.create_features(val_df_raw)
    val_df = preprocessor.create_labels(val_df)
    print("  ✓ Val features/labels created")
    sys.stdout.flush()

    # Create sequences per split to keep history/labels inside split
    print("  Step 5/5: Creating sequences and scaling...")
    sys.stdout.flush()
    feature_cols = preprocessor.get_feature_columns()

    X_train_raw, y_class_train, y_reg_train = preprocessor.create_sequences(train_df, feature_cols)
    X_val_raw, y_class_val, y_reg_val = preprocessor.create_sequences(val_df, feature_cols)

    print(f"  ✓ Created sequences (train={len(X_train_raw)}, val={len(X_val_raw)})")
    print(f"    Train label distribution: DOWN={np.sum(y_class_train==0)}, SIDEWAYS={np.sum(y_class_train==1)}, UP={np.sum(y_class_train==2)}")
    print(f"    Val label distribution:   DOWN={np.sum(y_class_val==0)}, SIDEWAYS={np.sum(y_class_val==1)}, UP={np.sum(y_class_val==2)}")
    sys.stdout.flush()

    # Fit scaler on training data only, then transform both splits
    preprocessor.fit_scaler(X_train_raw)
    X_train = preprocessor.transform(X_train_raw)
    X_val = preprocessor.transform(X_val_raw)

    # Create datasets
    train_dataset = TradingDataset(X_train, y_class_train, y_reg_train)
    val_dataset = TradingDataset(X_val, y_class_val, y_reg_val)

    # Create dataloaders with GPU optimizations
    train_loader = DataLoader(
        train_dataset,
        batch_size=batch_size,
        shuffle=True,
        num_workers=num_workers,
        pin_memory=True  # Faster GPU transfer
    )
    val_loader = DataLoader(
        val_dataset,
        batch_size=batch_size,
        shuffle=False,
        num_workers=num_workers,
        pin_memory=True
    )

    print(f"  ✓ Created dataloaders")
    print(f"    Train batches: {len(train_loader)}")
    print(f"    Val batches: {len(val_loader)}")
    sys.stdout.flush()

    return train_loader, val_loader, preprocessor


if __name__ == '__main__':
    # Example usage
    import sys
    sys.stdout.flush()  # Ensure output is written immediately

    print("Training LSTM/Transformer Trading Model")
    print("=" * 50)
    sys.stdout.flush()

    # Configuration - Optimized for RTX 5090 + 15 vCPUs
    MODEL_TYPE = 'transformer_lstm'  # Full transformer-LSTM model
    DATA_PATH = '../data/training_data.csv'  # Path relative to trading_model/
    SAVE_DIR = 'checkpoints'
    BATCH_SIZE = 256  # Larger batch for RTX 5090
    GRADIENT_ACCUM_STEPS = 1  # No need with 33GB VRAM
    EPOCHS = 200  # More epochs with early stopping
    LEARNING_RATE = 0.0002
    LOOKBACK = 50  # Start with 50, can increase later
    NUM_WORKERS = 8  # Use 8 workers to feed GPU faster

    print("\n" + "="*60)
    print("LOADING DATA...")
    print("="*60)
    print(f"Data path: {DATA_PATH}")
    print(f"Lookback: {LOOKBACK}")
    print(f"Batch size: {BATCH_SIZE}")
    print()

    try:
        # Prepare data - use all available data
        train_loader, val_loader, preprocessor = prepare_dataloaders(
            csv_path=DATA_PATH,
            batch_size=BATCH_SIZE,
            val_split=0.2,
            max_rows=None,  # Use all 69K rows
            lookback=LOOKBACK,
            num_workers=NUM_WORKERS
        )
        print("✓ Data loaded successfully!")
    except Exception as e:
        print(f"✗ FAILED to load data: {e}")
        import traceback
        traceback.print_exc()
        raise

    # Save preprocessor
    print("\n" + "="*60)
    print("SAVING PREPROCESSOR...")
    print("="*60)

    # Create checkpoint directory if it doesn't exist
    from pathlib import Path
    Path(SAVE_DIR).mkdir(exist_ok=True)

    preprocessor.save(f'{SAVE_DIR}/preprocessor.pkl')
    print(f"✓ Saved to {SAVE_DIR}/preprocessor.pkl")

    # Create model - Start with moderate size
    print("\n" + "="*60)
    print("CREATING MODEL...")
    print("="*60)
    input_size = len(preprocessor.feature_columns)
    print(f"Input features: {input_size}")

    try:
        if MODEL_TYPE == 'transformer_lstm':
            model = create_model(
                model_type=MODEL_TYPE,
                input_size=input_size,
                hidden_size=256,  # Moderate size
                num_lstm_layers=2,
                num_transformer_layers=3,
                num_heads=8,
                dropout=0.2
            )
        else:
            model = create_model(
                model_type=MODEL_TYPE,
                input_size=input_size,
                hidden_size=128,
                num_layers=2,
                dropout=0.2
            )

        param_count = sum(p.numel() for p in model.parameters())
        print(f"✓ Created {MODEL_TYPE} model")
        print(f"  Parameters: {param_count:,}")
    except Exception as e:
        print(f"✗ FAILED to create model: {e}")
        import traceback
        traceback.print_exc()
        raise

    # Create trainer
    print("\n" + "="*60)
    print("INITIALIZING TRAINER...")
    print("="*60)
    try:
        trainer = TradingModelTrainer(
            model=model,
            learning_rate=LEARNING_RATE,
            gradient_accumulation_steps=GRADIENT_ACCUM_STEPS,
            use_focal_loss=True,
            focal_gamma=1.5
        )
        print(f"✓ Trainer initialized")
        print(f"  Effective batch size: {BATCH_SIZE * GRADIENT_ACCUM_STEPS}")
    except Exception as e:
        print(f"✗ FAILED to initialize trainer: {e}")
        import traceback
        traceback.print_exc()
        raise

    # Train
    print("\n" + "="*60)
    print("STARTING TRAINING...")
    print("="*60)
    try:
        history = trainer.train(
            train_loader=train_loader,
            val_loader=val_loader,
            epochs=EPOCHS,
            save_dir=SAVE_DIR,
            early_stopping_patience=25
        )
        print("\n" + "="*60)
        print("TRAINING COMPLETE!")
        print("="*60)
    except Exception as e:
        print(f"\n✗ TRAINING FAILED: {e}")
        import traceback
        traceback.print_exc()
        raise

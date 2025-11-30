# Training Notes

## Fixes Applied

### 1. Deprecation Warning Fixed
- **File:** `trading_model/utils/preprocessor.py:110`
- **Change:** `df.fillna(method='ffill')` → `df.ffill()`
- **Reason:** Pandas deprecated the `method` parameter

### 2. Memory Optimization
To prevent "killed" errors (OOM - Out of Memory), the following optimizations were applied:

#### Model Configuration
- **Model Type:** Changed from `transformer_lstm` to `lightweight_lstm`
  - TransformerLSTM is more accurate but memory-intensive
  - LightweightLSTM is faster and uses less memory

- **Hidden Size:** Reduced from `128` to `64`
  - Fewer parameters = less memory
  - Still sufficient for trading predictions

- **Batch Size:** Increased from `32` to `64`
  - Larger batches are more memory efficient
  - Faster training

## Current Training Configuration

```python
MODEL_TYPE = 'lightweight_lstm'
HIDDEN_SIZE = 64
NUM_LAYERS = 2
BATCH_SIZE = 64
EPOCHS = 100
LEARNING_RATE = 0.001
LOOKBACK = 50  # Number of historical candles
```

## Training Tips

### If training still fails with OOM:

1. **Reduce data size:**
   ```python
   # In train.py, after loading the CSV:
   df = df.tail(50000)  # Use only last 50k rows
   ```

2. **Use smaller lookback:**
   ```python
   # In prepare_dataloaders():
   preprocessor = TradingDataPreprocessor(lookback=30)  # Reduced from 50
   ```

3. **Reduce batch size:**
   ```python
   BATCH_SIZE = 32  # or even 16
   ```

4. **Monitor memory usage:**
   ```bash
   # Watch memory during training
   watch -n 1 'ps aux | grep python'
   ```

### If you want better accuracy (and have more memory):

Switch back to the full model:
```python
MODEL_TYPE = 'transformer_lstm'
hidden_size = 128
num_lstm_layers = 2
num_transformer_layers = 2
num_heads = 4
```

## Expected Training Time

- **LightweightLSTM:** ~2-5 minutes per epoch (on CPU)
- **TransformerLSTM:** ~10-15 minutes per epoch (on CPU)

With early stopping (patience=15), training typically completes in 20-50 epochs.

## Model Performance Targets

- **Validation Accuracy:** Target >60% (baseline: 33% for 3-class)
- **Validation F1 Score:** Target >0.55
- **Class Balance:** Monitor per-class accuracy to avoid bias

## Troubleshooting

### Process Killed
- Symptom: `zsh: killed python train.py`
- Cause: Out of memory (OOM)
- Solution: Follow memory reduction tips above

### Poor Accuracy
- Symptom: Validation accuracy ~33% (random guessing)
- Possible causes:
  - Insufficient training data
  - Overfitting (training acc high, val acc low)
  - Need more epochs or different learning rate

### NaN Loss
- Symptom: Loss becomes NaN during training
- Cause: Gradient explosion or bad data
- Solution:
  - Check for NaN/Inf in training data
  - Reduce learning rate
  - Increase gradient clipping

## Next Steps After Training

1. Check `checkpoints/best_model.pt` - saved model
2. Check `checkpoints/preprocessor.pkl` - saved preprocessor
3. Check `checkpoints/history.json` - training metrics
4. Start prediction API: `python api/prediction_service.py`

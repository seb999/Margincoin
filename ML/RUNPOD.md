

### 1. ssd-add Copenhagen2025

### 2. Create a POD on Runpod.io

### 3. Connect to the pod and clone ML

```bash
cd workspace
git clone --no-checkout https://github.com/seb999/Margincoin
cd Margincoin
git sparse-checkout init --cone
git sparse-checkout set ML
git checkout main
```

### 4. Train

```bash
cd ML
pip install -r requirements.txt
python trading_model/utils/collect_data.py \
  --start-date 2020-01-01 \
  --end-date 2025-12-04 \
  --symbols BTCUSDC ETHUSDC BNBUSDC SOLUSDC ADAUSDC XRPUSDC \
  --interval 1h \
  --output data/training_data.csv
```

### 5. Connect to the pod and clone ML
```bash
scp -P 47337 -i Helsinki2025 -r \
root@149.36.1.233:/workspace/MarginCoin/ML/trading_model/checkpoints \
./checkpoints
```
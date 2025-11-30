"""Quick test to show how features are created"""
import pandas as pd
from utils.preprocessor import TradingDataPreprocessor

# Load raw data
df = pd.read_csv('../data/training_data.csv', nrows=100)
print("RAW CSV COLUMNS:")
print(df.columns.tolist())
print(f"\nNumber of columns in CSV: {len(df.columns)}")

# Create preprocessor
preprocessor = TradingDataPreprocessor(lookback=30, forward_bars=5, threshold=0.002)

# Add features
df = preprocessor.create_features(df)
print("\n" + "="*60)
print("AFTER create_features():")
print(df.columns.tolist())
print(f"\nNumber of columns: {len(df.columns)}")

# Add labels
df = preprocessor.create_labels(df)
print("\n" + "="*60)
print("AFTER create_labels():")
print(df.columns.tolist())
print(f"\nNumber of columns: {len(df.columns)}")

# Show feature columns used for model
feature_cols = preprocessor.get_feature_columns()
print("\n" + "="*60)
print("FEATURES USED FOR MODEL INPUT:")
for i, col in enumerate(feature_cols, 1):
    print(f"{i:2d}. {col}")
print(f"\nTotal features for model: {len(feature_cols)}")

# Show label distribution
print("\n" + "="*60)
print("LABEL DISTRIBUTION:")
print(df['label'].value_counts())

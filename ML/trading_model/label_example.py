"""Show how labels are created from FUTURE prices"""
import pandas as pd

# Create example data
data = {
    'timestamp': [f'T{i}' for i in range(10)],
    'close': [100, 102, 101, 105, 103, 108, 107, 110, 109, 112]
}
df = pd.DataFrame(data)

print("ORIGINAL DATA:")
print(df)
print()

# Calculate forward return (look 5 bars ahead)
forward_bars = 5
threshold = 0.02  # 2%

df['future_close'] = df['close'].shift(-forward_bars)
df['forward_return'] = (df['future_close'] / df['close']) - 1

# Create labels
df['label'] = 'SIDEWAYS'
df.loc[df['forward_return'] > threshold, 'label'] = 'UP'
df.loc[df['forward_return'] < -threshold, 'label'] = 'DOWN'

print("="*70)
print("AFTER LABEL CREATION:")
print(df[['timestamp', 'close', 'future_close', 'forward_return', 'label']])
print()

print("="*70)
print("EXPLANATION FOR T0:")
print(f"  Current price (T0):  {df.loc[0, 'close']}")
print(f"  Future price (T5):   {df.loc[0, 'future_close']}")
print(f"  Forward return:      {df.loc[0, 'forward_return']:.2%}")
print(f"  Label:               {df.loc[0, 'label']}")
print()
print("  → The model learns: 'When I see patterns at T0, price will go UP by T5'")

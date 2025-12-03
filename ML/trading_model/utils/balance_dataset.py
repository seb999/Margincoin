"""
Balance trading dataset to equal class distribution
Undersamples majority classes to achieve 1/3-1/3-1/3 split
"""

import pandas as pd
import numpy as np
from pathlib import Path
import argparse


def balance_dataset(
    input_csv: str,
    output_csv: str,
    target_ratio: tuple = (1/3, 1/3, 1/3),
    threshold: float = 0.002,
    forward_bars: int = 5,
    random_seed: int = 42
):
    """
    Balance dataset by undersampling majority classes

    Args:
        input_csv: Path to unbalanced CSV with OHLCV data
        output_csv: Path to save balanced CSV
        target_ratio: Desired ratio for (DOWN, SIDEWAYS, UP)
        threshold: Price change threshold for classification
        forward_bars: Number of bars ahead for prediction
        random_seed: Random seed for reproducibility

    Returns:
        DataFrame with balanced classes
    """
    np.random.seed(random_seed)

    print("="*60)
    print("DATASET BALANCING")
    print("="*60)

    # Load data
    print(f"\n1. Loading data from {input_csv}")
    df = pd.read_csv(input_csv)
    print(f"   Loaded {len(df)} rows")

    # Calculate forward returns
    print(f"\n2. Calculating forward returns (threshold={threshold*100:.2f}%)")
    df = df.sort_values(['symbol', 'timestamp'])
    df['forward_return'] = df.groupby('symbol')['close'].transform(
        lambda x: (x.shift(-forward_bars) / x) - 1
    )

    # Create labels
    df['label'] = 'SIDEWAYS'
    df.loc[df['forward_return'] > threshold, 'label'] = 'UP'
    df.loc[df['forward_return'] < -threshold, 'label'] = 'DOWN'

    # Remove rows without forward return (last N bars)
    df = df.dropna(subset=['forward_return'])

    # Show original distribution
    print(f"\n3. Original class distribution:")
    class_counts = df['label'].value_counts()
    total = len(df)
    for label in ['DOWN', 'SIDEWAYS', 'UP']:
        count = class_counts.get(label, 0)
        pct = (count / total) * 100
        print(f"   {label:10s}: {count:7,} ({pct:5.2f}%)")

    # Find minority class size
    min_class_size = class_counts.min()

    # Calculate target sizes based on desired ratio
    total_target = min_class_size * 3  # Ensure we can achieve 1/3-1/3-1/3
    target_down = int(total_target * target_ratio[0])
    target_sideways = int(total_target * target_ratio[1])
    target_up = int(total_target * target_ratio[2])

    print(f"\n4. Target sizes for balanced dataset:")
    print(f"   DOWN:     {target_down:7,}")
    print(f"   SIDEWAYS: {target_sideways:7,}")
    print(f"   UP:       {target_up:7,}")
    print(f"   Total:    {total_target:7,}")

    # Sample from each class - PRESERVE TIME ORDER
    print(f"\n5. Sampling from each class (preserving time order)...")

    # Get indices for each class
    down_indices = df[df['label'] == 'DOWN'].index.tolist()
    sideways_indices = df[df['label'] == 'SIDEWAYS'].index.tolist()
    up_indices = df[df['label'] == 'UP'].index.tolist()

    # Randomly sample indices
    np.random.shuffle(down_indices)
    np.random.shuffle(sideways_indices)
    np.random.shuffle(up_indices)

    selected_down = down_indices[:min(target_down, len(down_indices))]
    selected_sideways = sideways_indices[:min(target_sideways, len(sideways_indices))]
    selected_up = up_indices[:min(target_up, len(up_indices))]

    # Combine indices and sort to preserve time order
    all_selected_indices = selected_down + selected_sideways + selected_up
    all_selected_indices.sort()

    # Select rows maintaining time order
    balanced_df = df.loc[all_selected_indices].reset_index(drop=True)

    print(f"   ✓ Selected {len(balanced_df)} candles in chronological order")

    # Remove the temporary label columns (training script will recalculate)
    balanced_df = balanced_df.drop(columns=['forward_return', 'label'])

    # Show final distribution
    print(f"\n6. Saving balanced dataset to {output_csv}")

    # Save
    output_path = Path(output_csv)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    balanced_df.to_csv(output_csv, index=False)

    print(f"\n   ✓ Saved {len(balanced_df)} rows")
    print(f"   Symbols: {balanced_df['symbol'].nunique()}")
    if 'timestamp' in balanced_df.columns:
        dates = pd.to_datetime(balanced_df['timestamp'], unit='s')
        print(f"   Date range: {dates.min()} to {dates.max()}")

    print("\n" + "="*60)
    print("BALANCING COMPLETE!")
    print("="*60)
    print(f"\nNext step: Train with balanced data")
    print(f"  cd trading_model")
    print(f"  python train.py")

    return balanced_df


def main():
    parser = argparse.ArgumentParser(
        description='Balance trading dataset to equal class distribution',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Example:
  # Balance the dataset to 1/3-1/3-1/3
  python balance_dataset.py \\
    --input data/training_data.csv \\
    --output data/training_data_balanced.csv
        """
    )

    parser.add_argument(
        '--input',
        type=str,
        required=True,
        help='Input CSV path (unbalanced data)'
    )

    parser.add_argument(
        '--output',
        type=str,
        required=True,
        help='Output CSV path (balanced data)'
    )

    parser.add_argument(
        '--threshold',
        type=float,
        default=0.002,
        help='Price change threshold (default: 0.002 = 0.2%%)'
    )

    parser.add_argument(
        '--forward-bars',
        type=int,
        default=5,
        help='Number of bars ahead for prediction (default: 5)'
    )

    parser.add_argument(
        '--seed',
        type=int,
        default=42,
        help='Random seed for reproducibility (default: 42)'
    )

    args = parser.parse_args()

    balance_dataset(
        input_csv=args.input,
        output_csv=args.output,
        threshold=args.threshold,
        forward_bars=args.forward_bars,
        random_seed=args.seed
    )


if __name__ == '__main__':
    main()

"""
Data collection script to export candle data from C# application
This script can be called from C# or run standalone to collect historical data
"""

import pandas as pd
import requests
from typing import List, Dict, Optional
import json
from datetime import datetime, timedelta
from pathlib import Path
import argparse
import time


def collect_from_json_file(json_path: str, output_path: str = None):
    """
    Convert JSON candle data exported from C# to CSV for training

    Expected JSON format:
    [
        {
            "symbol": "BTCUSDT",
            "candles": [
                {
                    "o": 50000, "h": 51000, "l": 49500, "c": 50500,
                    "v": 1000, "Rsi": 55, "Macd": 0.5,
                    "MacdSign": 0.3, "MacdHist": 0.2, "Ema": 50200,
                    "StochSlowK": 60, "StochSlowD": 55
                },
                ...
            ]
        },
        ...
    ]

    Args:
        json_path: Path to JSON file
        output_path: Output CSV path (default: data/training_data.csv)
    """
    if output_path is None:
        output_path = "data/training_data.csv"

    # Load JSON
    with open(json_path, 'r') as f:
        data = json.load(f)

    all_candles = []

    # Process each symbol
    for symbol_data in data:
        symbol = symbol_data.get('symbol', 'UNKNOWN')
        candles = symbol_data.get('candles', [])

        for candle in candles:
            # Normalize field names (handle both Python and C# naming conventions)
            normalized = {
                'symbol': symbol,
                'timestamp': candle.get('T', candle.get('t', datetime.now().timestamp())),
                'open': candle.get('o', candle.get('open', 0)),
                'high': candle.get('h', candle.get('high', 0)),
                'low': candle.get('l', candle.get('low', 0)),
                'close': candle.get('c', candle.get('close', 0)),
                'volume': candle.get('v', candle.get('volume', 0)),
                'rsi': candle.get('Rsi', candle.get('rsi', 50)),
                'macd': candle.get('Macd', candle.get('macd', 0)),
                'macd_signal': candle.get('MacdSign', candle.get('macd_signal', 0)),
                'macd_hist': candle.get('MacdHist', candle.get('macd_hist', 0)),
                'ema50': candle.get('Ema', candle.get('ema50', candle.get('close', 0))),
                'stoch_k': candle.get('StochSlowK', candle.get('stoch_k', 50)),
                'stoch_d': candle.get('StochSlowD', candle.get('stoch_d', 50))
            }
            all_candles.append(normalized)

    # Convert to DataFrame
    df = pd.DataFrame(all_candles)

    # Sort by symbol and timestamp
    df = df.sort_values(['symbol', 'timestamp'])

    # Save to CSV
    output_dir = Path(output_path).parent
    output_dir.mkdir(parents=True, exist_ok=True)

    df.to_csv(output_path, index=False)
    print(f"Saved {len(df)} candles to {output_path}")
    print(f"Symbols: {df['symbol'].nunique()}")
    print(f"Date range: {pd.to_datetime(df['timestamp'], unit='s').min()} to {pd.to_datetime(df['timestamp'], unit='s').max()}")

    return df


def collect_from_binance_api(
    symbols: List[str],
    interval: str = '30m',
    start_date: Optional[str] = None,
    end_date: Optional[str] = None,
    months: Optional[int] = None
):
    """
    Collect historical data directly from Binance API with automatic batching
    Handles Binance's 1000 candle limit by making multiple requests

    Args:
        symbols: List of trading pairs (e.g., ['BTCUSDT', 'ETHUSDT'])
        interval: Candle interval ('15m', '30m', '1h', '1d', etc.)
        start_date: Start date in YYYY-MM-DD format (optional)
        end_date: End date in YYYY-MM-DD format (optional)
        months: Number of months of data to fetch (alternative to dates)

    Returns:
        DataFrame with OHLCV data and calculated indicators
    """
    # Calculate date range
    if start_date and end_date:
        start_time = int(datetime.strptime(start_date, '%Y-%m-%d').timestamp() * 1000)
        end_time = int(datetime.strptime(end_date, '%Y-%m-%d').timestamp() * 1000)
    elif months:
        end_time = int(datetime.now().timestamp() * 1000)
        start_time = int((datetime.now() - timedelta(days=months * 30)).timestamp() * 1000)
    else:
        # Default: fetch last 1000 candles
        return collect_from_binance_simple(symbols, interval, 1000)

    print(f"Fetching data from {datetime.fromtimestamp(start_time/1000)} to {datetime.fromtimestamp(end_time/1000)}")
    print(f"Interval: {interval}")

    all_data = []

    for symbol in symbols:
        print(f"\n{'='*60}")
        print(f"Fetching {symbol}...")
        print(f"{'='*60}")

        symbol_data = fetch_symbol_with_batching(
            symbol=symbol,
            interval=interval,
            start_time=start_time,
            end_time=end_time
        )

        all_data.extend(symbol_data)

    # Convert to DataFrame
    df = pd.DataFrame(all_data)

    if len(df) == 0:
        print("No data fetched!")
        return df

    # Sort by symbol and timestamp
    df = df.sort_values(['symbol', 'timestamp'])

    # Calculate indicators
    print("\n" + "="*60)
    print("Calculating technical indicators...")
    print("="*60)
    df = calculate_indicators(df)

    return df


def collect_from_binance_simple(symbols: List[str], interval: str = '30m', limit: int = 1000):
    """
    Simple collection without date ranges (backward compatibility)

    Args:
        symbols: List of trading pairs
        interval: Candle interval
        limit: Number of candles to fetch (max 1000)
    """
    all_data = []

    for symbol in symbols:
        print(f"Fetching {symbol}...")

        try:
            url = "https://api.binance.com/api/v3/klines"
            params = {
                'symbol': symbol,
                'interval': interval,
                'limit': limit
            }

            response = requests.get(url, params=params)
            response.raise_for_status()

            klines = response.json()

            for kline in klines:
                candle = {
                    'symbol': symbol,
                    'timestamp': kline[0] / 1000,
                    'open': float(kline[1]),
                    'high': float(kline[2]),
                    'low': float(kline[3]),
                    'close': float(kline[4]),
                    'volume': float(kline[5]),
                    'rsi': 50,
                    'macd': 0,
                    'macd_signal': 0,
                    'macd_hist': 0,
                    'ema50': float(kline[4]),
                    'stoch_k': 50,
                    'stoch_d': 50
                }
                all_data.append(candle)

            print(f"  Fetched {len(klines)} candles")

        except Exception as e:
            print(f"  Error fetching {symbol}: {e}")

    df = pd.DataFrame(all_data)
    print("\nCalculating indicators...")
    df = calculate_indicators(df)

    return df


def fetch_symbol_with_batching(
    symbol: str,
    interval: str,
    start_time: int,
    end_time: int
) -> List[Dict]:
    """
    Fetch historical data for a symbol with automatic batching
    Handles Binance's 1000 candle limit per request

    Args:
        symbol: Trading pair (e.g., 'BTCUSDT')
        interval: Candle interval
        start_time: Start timestamp in milliseconds
        end_time: End timestamp in milliseconds

    Returns:
        List of candle dictionaries
    """
    url = "https://api.binance.com/api/v3/klines"
    all_candles = []
    current_start = start_time
    batch_num = 1

    while current_start < end_time:
        try:
            params = {
                'symbol': symbol,
                'interval': interval,
                'startTime': current_start,
                'endTime': end_time,
                'limit': 1000
            }

            print(f"  Batch {batch_num}: Fetching from {datetime.fromtimestamp(current_start/1000)}", end='')

            response = requests.get(url, params=params)
            response.raise_for_status()

            klines = response.json()

            if len(klines) == 0:
                print(" - No more data")
                break

            # Convert to candle format
            for kline in klines:
                candle = {
                    'symbol': symbol,
                    'timestamp': kline[0] / 1000,
                    'open': float(kline[1]),
                    'high': float(kline[2]),
                    'low': float(kline[3]),
                    'close': float(kline[4]),
                    'volume': float(kline[5]),
                    'rsi': 50,
                    'macd': 0,
                    'macd_signal': 0,
                    'macd_hist': 0,
                    'ema50': float(kline[4]),
                    'stoch_k': 50,
                    'stoch_d': 50
                }
                all_candles.append(candle)

            print(f" - Got {len(klines)} candles (Total: {len(all_candles)})")

            # Update start time for next batch (use last candle's close time + 1ms)
            current_start = klines[-1][6] + 1

            batch_num += 1

            # Rate limiting - Binance allows 1200 requests per minute
            # Sleep for 0.1s between requests to be safe (600 req/min)
            time.sleep(0.1)

        except Exception as e:
            print(f"\n  Error fetching batch: {e}")
            break

    print(f"  ✓ Total fetched: {len(all_candles)} candles for {symbol}")
    return all_candles


def calculate_indicators(df: pd.DataFrame) -> pd.DataFrame:
    """
    Calculate technical indicators for raw OHLCV data
    Uses pandas-ta or manual calculations

    Args:
        df: DataFrame with OHLCV columns

    Returns:
        DataFrame with indicator columns added
    """
    try:
        import pandas_ta as ta

        # Group by symbol to calculate indicators per symbol
        result_dfs = []

        for symbol in df['symbol'].unique():
            symbol_df = df[df['symbol'] == symbol].copy()

            # RSI
            symbol_df['rsi'] = ta.rsi(symbol_df['close'], length=14)

            # MACD
            macd = ta.macd(symbol_df['close'], fast=12, slow=26, signal=9)
            symbol_df['macd'] = macd['MACD_12_26_9']
            symbol_df['macd_signal'] = macd['MACDs_12_26_9']
            symbol_df['macd_hist'] = macd['MACDh_12_26_9']

            # EMA
            symbol_df['ema50'] = ta.ema(symbol_df['close'], length=50)

            # Stochastic
            stoch = ta.stoch(symbol_df['high'], symbol_df['low'], symbol_df['close'], k=5, d=3)
            symbol_df['stoch_k'] = stoch[f'STOCHk_5_3_3']
            symbol_df['stoch_d'] = stoch[f'STOCHd_5_3_3']

            result_dfs.append(symbol_df)

        df = pd.concat(result_dfs, ignore_index=True)

    except ImportError:
        print("Warning: pandas_ta not installed. Using simple calculations.")
        # Simple EMA calculation
        df['ema50'] = df.groupby('symbol')['close'].transform(lambda x: x.ewm(span=50).mean())
        # Fill other indicators with defaults
        df['rsi'] = 50
        df['macd'] = 0
        df['macd_signal'] = 0
        df['macd_hist'] = 0
        df['stoch_k'] = 50
        df['stoch_d'] = 50

    # Fill NaN values
    df = df.bfill().ffill().fillna(0)

    return df


def main():
    """Command-line interface for data collection"""
    parser = argparse.ArgumentParser(
        description='Collect trading data for model training',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Fetch 6 months of data
  python collect_data.py --source binance --months 6 --symbols BTCUSDT ETHUSDT

  # Fetch specific date range
  python collect_data.py --source binance --start-date 2024-01-01 --end-date 2024-12-01

  # Fetch from JSON file
  python collect_data.py --source json --json-path data/export.json
        """
    )

    parser.add_argument(
        '--source',
        choices=['json', 'binance'],
        default='binance',
        help='Data source (json file or binance API)'
    )

    parser.add_argument(
        '--json-path',
        type=str,
        help='Path to JSON file (for json source)'
    )

    parser.add_argument(
        '--symbols',
        nargs='+',
        default=['BTCUSDT', 'ETHUSDT', 'BNBUSDT'],
        help='Symbols to fetch (for binance source)'
    )

    parser.add_argument(
        '--interval',
        type=str,
        default='30m',
        help='Candle interval: 1m, 5m, 15m, 30m, 1h, 4h, 1d (for binance source)'
    )

    parser.add_argument(
        '--start-date',
        type=str,
        help='Start date in YYYY-MM-DD format (for binance source)'
    )

    parser.add_argument(
        '--end-date',
        type=str,
        help='End date in YYYY-MM-DD format (for binance source)'
    )

    parser.add_argument(
        '--months',
        type=int,
        help='Number of months to fetch (alternative to date range, for binance source)'
    )

    parser.add_argument(
        '--output',
        type=str,
        default='data/training_data.csv',
        help='Output CSV path'
    )

    args = parser.parse_args()

    if args.source == 'json':
        if not args.json_path:
            print("Error: --json-path required for json source")
            return

        df = collect_from_json_file(args.json_path, args.output)

    elif args.source == 'binance':
        df = collect_from_binance_api(
            symbols=args.symbols,
            interval=args.interval,
            start_date=args.start_date,
            end_date=args.end_date,
            months=args.months
        )

        # Save
        output_dir = Path(args.output).parent
        output_dir.mkdir(parents=True, exist_ok=True)

        df.to_csv(args.output, index=False)
        print(f"\n{'='*60}")
        print(f"Saved {len(df)} candles to {args.output}")
        print(f"{'='*60}")

    print("\nData collection complete!")
    print(f"Total candles: {len(df)}")
    print(f"Symbols: {df['symbol'].nunique()}")
    if len(df) > 0:
        print(f"Date range: {pd.to_datetime(df['timestamp'], unit='s').min()} to {pd.to_datetime(df['timestamp'], unit='s').max()}")
    print(f"\nNext step: Run 'python trading_model/train.py' to train the model")


if __name__ == '__main__':
    main()

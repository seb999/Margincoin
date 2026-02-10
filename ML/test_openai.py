#!/usr/bin/env python3
"""Test script for OpenAI endpoint"""

import requests
import json

# Test data with realistic crypto indicators
test_request = {
    "symbol": "BTCUSDT",
    "indicators": {
        "close": 45000.0,
        "rsi": 65.5,
        "macd": 250.5,
        "macd_signal": 200.3,
        "macd_hist": 50.2,
        "ema50": 44500.0,
        "volume": 1500000.0,
        "stoch_k": 70.0,
        "stoch_d": 65.0
    },
    "previous_indicators": {
        "close": 44800.0,
        "rsi": 62.0,
        "macd_hist": 30.5
    }
}

# Make request to local API
url = "http://localhost:8000/predict/openai"

try:
    print("Testing OpenAI endpoint...")
    print(f"Request: {json.dumps(test_request, indent=2)}")
    print("\nSending request to:", url)

    response = requests.post(url, json=test_request, timeout=30)

    if response.status_code == 200:
        result = response.json()
        print("\n✅ SUCCESS!")
        print(f"\nSymbol: {result['symbol']}")
        print(f"Signal: {result['signal']}")
        print(f"Trading Score: {result['trading_score']}/10")
        print(f"Confidence: {result['confidence']:.2%}")
        print(f"Risk Level: {result['risk_level']}")
        print(f"\nKey Factors:")
        for factor in result['key_factors']:
            print(f"  - {factor}")
        print(f"\nReasoning:\n{result['reasoning']}")
    else:
        print(f"\n❌ ERROR: {response.status_code}")
        print(response.text)

except requests.exceptions.ConnectionError:
    print("\n❌ Connection Error: Make sure the API is running on http://localhost:8000")
    print("Start it with: cd ML/api && python prediction_service.py")
except Exception as e:
    print(f"\n❌ Error: {e}")

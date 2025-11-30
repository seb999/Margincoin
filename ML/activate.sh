#!/bin/bash
# Helper script to activate the Python virtual environment
# Usage: source activate.sh

source venv/bin/activate
echo "Python virtual environment activated!"
echo "Python version: $(python --version)"
echo ""
echo "Available commands:"
echo "  - python trading_model/train.py          # Train the model"
echo "  - python trading_model/utils/collect_data.py  # Collect training data"
echo "  - python trading_model/api/prediction_service.py  # Start prediction API"
echo ""
echo "To deactivate, run: deactivate"

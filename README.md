# MarginCoin - Cryptocurrency Trading Bot

A cryptocurrency margin trading bot with machine learning capabilities that automatically trades on the Binance cryptocurrency exchange.

## Tech Stack

### Backend
- **Framework:** ASP.NET Core 6.0 (C#)
- **Database:** SQLite with Entity Framework Core
- **Real-time:** SignalR for WebSocket communication
- **Trading APIs:** Binance.Net, Binance.Spot
- **Machine Learning:** Microsoft.ML, TensorFlow.NET
- **Logging:** Serilog

### Frontend
- **Framework:** Angular 17
- **UI Components:** Angular Material 17, Bootstrap 5
- **Real-time:** SignalR Client
- **Charts:** Highcharts

## Prerequisites

- .NET 6.0 SDK
- Node.js (v18 or higher)
- Binance API keys (testnet and/or production)
- CoinMarketCap API key

## Configuration

### API Keys Setup

**IMPORTANT:** API keys are no longer hardcoded in the source code. You must configure them in `appsettings.Local.json`.

1. Copy the example configuration file:
   ```bash
   cp appsettings.Local.json.example appsettings.Local.json
   ```

2. Edit `appsettings.Local.json` and add your API keys:
   ```json
   {
     "Binance": {
       "Test": {
         "PublicKey": "YOUR_BINANCE_TESTNET_PUBLIC_KEY",
         "SecretKey": "YOUR_BINANCE_TESTNET_SECRET_KEY"
       },
       "Production": {
         "PublicKey": "YOUR_BINANCE_PRODUCTION_PUBLIC_KEY",
         "SecretKey": "YOUR_BINANCE_PRODUCTION_SECRET_KEY"
       }
     },
     "CoinMarketCap": {
       "ApiKey": "YOUR_COINMARKETCAP_API_KEY"
     }
   }
   ```

3. **NEVER commit `appsettings.Local.json` to version control** (it's already in .gitignore)

### Getting API Keys

- **Binance Testnet:** https://testnet.binance.vision/
- **Binance Production:** https://www.binance.com/en/my/settings/api-management
- **CoinMarketCap:** https://coinmarketcap.com/api/

## Installation

### Backend Setup

```bash
# Restore NuGet packages
dotnet restore

# Build the project
dotnet build
```

### Frontend Setup

```bash
# Navigate to Angular app
cd ClientApp

# Install dependencies
npm install

# Build for production
npm run build
```

## Running the Application

### Development Mode

1. **Start the Angular development server:**
   ```bash
   cd ClientApp
   npm start
   ```
   This runs on http://localhost:4201

2. **Start the .NET backend:**
   ```bash
   dotnet run
   ```
   This runs on https://localhost:5002

3. Open your browser to https://localhost:5002

### Production Mode

```bash
# Build Angular app
cd ClientApp
npm run build

# Run the .NET app in production mode
cd ..
dotnet run --environment Production
```

In production mode, the compiled Angular files from `ClientApp/dist/` are served directly by the ASP.NET Core app.

## Database

The application uses SQLite with the database file `MarginCoinData.db`. The database is automatically created on first run.

**Note:** The database file is excluded from version control. Each developer needs their own local database.

## Project Structure

```
MarginCoin/
├── Configuration/          # Configuration classes for API keys
├── Controllers/            # API endpoints
│   ├── AlgoTradeController.cs  # Main trading algorithm
│   ├── BinanceController.cs    # Binance operations
│   ├── ActionController.cs     # Symbol management
│   └── OrderController.cs      # Order queries
├── Service/                # Business logic layer
│   ├── BinanceService.cs       # Binance API wrapper
│   ├── OrderService.cs         # Order management
│   ├── MLService.cs            # Machine learning
│   └── WebSocket.cs            # WebSocket handling
├── Model/                  # Entity Framework models
├── Class/                  # Data transfer objects
├── Misc/                   # Utilities and helpers
├── ClientApp/              # Angular 17 frontend
│   ├── src/app/
│   │   ├── components/         # UI components
│   │   ├── service/            # Angular services
│   │   └── pages/              # Page components
│   └── dist/                   # Built Angular app (production)
└── appsettings.json        # Base configuration (no secrets)
```

## Key Features

- **Algorithmic Trading:** Automated trading based on technical indicators (MACD, RSI, EMA, Stochastic, ATR)
- **Real-time Market Data:** WebSocket streaming of Binance market data
- **Machine Learning:** Image-based price prediction using TensorFlow
- **Order Management:** Buy/Sell with market and limit orders
- **Take Profit / Stop Loss:** Automated risk management
- **Multi-symbol Trading:** Monitor and trade multiple cryptocurrency pairs
- **Real-time Dashboard:** Live updates via SignalR

## Security Notes

### Recent Security Improvements

1. **API keys removed from source code** - Now configured via appsettings.Local.json
2. **Production SPA configuration fixed** - Production no longer depends on dev server
3. **Async/await patterns corrected** - Fixed deadlock risks in controllers
4. **Database excluded from git** - Prevents accidental commit of trading data

### Best Practices

- Never commit API keys to version control
- Use testnet for development and testing
- Keep production and testnet keys separate
- Regularly rotate API keys
- Use IP restrictions on Binance API keys
- Enable 2FA on your Binance account
- Review all trades before enabling production mode

## Trading Configuration

Default trading parameters (configured in AlgoTradeController.cs):

- **Interval:** 30-minute candles
- **Symbols:** Top 18 by market cap
- **Order Size:** 1500 USDT per trade
- **Max Open Trades:** 3
- **Stop Loss:** 2%

## Logging

Logs are written to:
- Console (development)
- File: `logs/` directory with daily rolling
- Seq (if configured): http://localhost:5004

## Troubleshooting

### Build Errors

**Issue:** Configuration classes not found
**Solution:** Ensure you've run `dotnet restore` and the Configuration folder exists

**Issue:** Angular Material version mismatch
**Solution:** Run `npm install` in the ClientApp directory

### Runtime Errors

**Issue:** "Binance API authentication failed"
**Solution:** Check your API keys in appsettings.Local.json

**Issue:** "Database locked"
**Solution:** Close any other instances of the application

**Issue:** "Angular dev server not running" (in development)
**Solution:** Make sure `npm start` is running in ClientApp directory

## Contributing

When contributing:

1. Never commit `appsettings.Local.json`
2. Never commit the database file `MarginCoinData.db`
3. Test with Binance testnet before production
4. Follow async/await best practices
5. Add logging for debugging

## Disclaimer

**Use at your own risk.** Cryptocurrency trading involves substantial risk of loss. This software is provided "as is" without warranty of any kind. The authors are not responsible for any financial losses incurred through the use of this software.

## License

[Add your license here]

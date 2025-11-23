# Changelog - Security and Code Quality Fixes

## Date: 2025-11-19

### Critical Security Fixes

#### 1. Removed Hardcoded API Keys
**Issue:** API keys for Binance (testnet and production) and CoinMarketCap were hardcoded in source code, creating a major security vulnerability.

**Files Changed:**
- [Service/BinanceService.cs](Service/BinanceService.cs)
- [Service/IBinanceService.cs](Service/IBinanceService.cs)
- [Controllers/ActionController.cs](Controllers/ActionController.cs)
- [appsettings.json](appsettings.json)

**Solution:**
- Created `Configuration/BinanceConfiguration.cs` with strongly-typed configuration classes
- Modified `BinanceService` to use dependency injection for API keys via `IOptions<BinanceConfiguration>`
- Modified `ActionController` to use dependency injection for CoinMarketCap API key via `IOptions<CoinMarketCapConfiguration>`
- Updated `Startup.cs` to register configuration options
- Added configuration sections to `appsettings.json`
- Created `appsettings.Local.json.example` as a template for developers

**Action Required:**
Developers must create `appsettings.Local.json` and add their API keys:
```json
{
  "Binance": {
    "Test": {
      "PublicKey": "YOUR_KEY_HERE",
      "SecretKey": "YOUR_SECRET_HERE"
    },
    "Production": {
      "PublicKey": "YOUR_KEY_HERE",
      "SecretKey": "YOUR_SECRET_HERE"
    }
  },
  "CoinMarketCap": {
    "ApiKey": "YOUR_KEY_HERE"
  }
}
```

#### 2. Fixed Production SPA Configuration
**Issue:** Production environment was configured to use Angular dev server (localhost:4201), which would fail in production deployment.

**File Changed:**
- [Startup.cs:111-113](Startup.cs#L111-L113)

**Solution:**
Removed the production-specific `UseProxyToSpaDevelopmentServer` configuration. Production now serves the compiled Angular files from `ClientApp/dist/` directory as intended.

**Before:**
```csharp
if (env.IsProduction())
{
    spa.UseProxyToSpaDevelopmentServer("http://localhost:4201");
}
```

**After:**
Production mode now correctly serves static files from dist folder (default behavior).

#### 3. Removed Public Static Mutable Credentials
**Issue:** API keys and secrets were stored in public static mutable fields, creating thread-safety issues and violating encapsulation.

**File Changed:**
- [Service/BinanceService.cs:24-26](Service/BinanceService.cs#L24-L26)
- [Service/IBinanceService.cs:10-17](Service/IBinanceService.cs#L10-L17)

**Solution:**
- Converted static fields to instance fields
- Made fields private
- Credentials now injected via configuration and stored in instance fields
- Removed static fields from interface (interfaces shouldn't contain state)

### Code Quality Fixes

#### 4. Fixed Async/Await Anti-Patterns
**Issue:** Controller methods were using `async void` instead of `async Task`, which can cause unhandled exceptions and deadlocks.

**File Changed:**
- [Controllers/BinanceController.cs](Controllers/BinanceController.cs)

**Methods Fixed:**
- `Sell(string symbol, double qty)` - Line 80
- `Buy(string symbol, double quoteQty)` - Line 109
- `CancelSymbolOrder(string symbol)` - Line 141

**Before:**
```csharp
public async void Buy(string symbol, double quoteQty)
```

**After:**
```csharp
public async Task Buy(string symbol, double quoteQty)
```

**Impact:** Prevents potential deadlocks and ensures exceptions are properly propagated.

#### 5. Fixed Dependency Injection for ActionController
**Issue:** `ActionController` was being instantiated directly with `new`, bypassing dependency injection and causing build errors after configuration changes.

**Files Changed:**
- [Controllers/AlgoTradeController.cs:106](Controllers/AlgoTradeController.cs#L106)
- [Controllers/AlgoTradeController.cs:338](Controllers/AlgoTradeController.cs#L338)
- [Controllers/GlobalsController.cs:44](Controllers/GlobalsController.cs#L44)
- [Startup.cs:38](Startup.cs#L38)

**Solution:**
- Registered `ActionController` as a scoped service in `Startup.cs`
- Injected `ActionController` into constructors instead of creating new instances
- Updated all usages to use the injected instance

#### 6. Updated Angular Material to v17
**Issue:** Angular Material 16.2.1 was incompatible with Angular 17.0.9, causing version mismatch warnings.

**File Changed:**
- [ClientApp/package.json:20-21](ClientApp/package.json#L20-L21)

**Before:**
```json
"@angular/material": "^16.2.1"
```

**After:**
```json
"@angular/material": "^17.0.4",
"@angular/cdk": "^17.0.4"
```

**Action Required:**
Run `npm install` in the `ClientApp` directory to install the updated packages.

### Documentation

#### 7. Created Comprehensive README
**File Created:**
- [README.md](README.md)

**Contents:**
- Project overview and tech stack
- Prerequisites and installation instructions
- **Security-focused configuration guide for API keys**
- Running the application (development and production)
- Project structure
- Key features
- Security best practices
- Troubleshooting guide

#### 8. Created Configuration Example
**File Created:**
- [appsettings.Local.json.example](appsettings.Local.json.example)

Template file showing developers exactly what configuration values they need to provide.

### Remaining Issues (Not Fixed)

The following issues were identified but not addressed in this session:

1. **SixLabors.ImageSharp Security Vulnerabilities** - Version 2.1.3 has multiple known vulnerabilities (high and moderate severity). Recommend upgrading to latest version.

2. **Blocking Async Operations** - `HttpHelper.cs` contains `.Result` calls that block threads. Should be converted to proper async/await patterns.

3. **Global Static State** - `Misc/Global.cs` uses extensive static fields for shared state, causing thread-safety concerns and testability issues.

4. **No Input Validation** - API endpoints lack input validation, creating potential security risks.

5. **Database in Git** - `MarginCoinData.db` is currently tracked in git (though .gitignore already excludes it, so new instances won't be added).

6. **Machine Learning File Paths** - Hardcoded paths to user's Downloads folder in `MLService.cs`.

## Build Status

✅ **Build Succeeded** - All changes compile successfully with no errors.

⚠️ **Warnings Present** - Build shows 20 warnings (mostly security vulnerabilities in dependencies).

## Migration Guide

### For Existing Developers

1. **Pull the latest changes**
2. **Create your local configuration:**
   ```bash
   cp appsettings.Local.json.example appsettings.Local.json
   ```
3. **Edit `appsettings.Local.json`** and add your API keys
4. **Update Angular dependencies:**
   ```bash
   cd ClientApp
   npm install
   ```
5. **Build and run:**
   ```bash
   dotnet build
   dotnet run
   ```

### For New Developers

Follow the complete setup instructions in [README.md](README.md).

## Summary

This update significantly improves the security posture of the MarginCoin application by removing hardcoded credentials and implementing proper configuration management. The async/await fixes prevent potential runtime issues, and the dependency injection improvements make the codebase more maintainable and testable.

**Most Important:** Never commit `appsettings.Local.json` - it contains your API keys and is already in .gitignore.

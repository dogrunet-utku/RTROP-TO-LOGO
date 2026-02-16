### 🏛️ Architect's Audit Report

#### 🚨 Critical Issues (Must Fix Immediately)
- **Logic Mismatches:**
  - **Stock Check Logic:** In `ProcessMrpCommandHandler.cs`, the code calculates `need = item.ROP_update_OrderQuantity - ropGap` where `ropGap = item.ROP - netStock`. If `netStock > item.ROP` (surplus), `ropGap` is negative, making `need` larger than `OrderQuantity`. The code then checks `if (need > 0)` which is true. This results in ordering *more* stock when there is already a surplus. The legacy code explicitly checks `if ((invTotal+openPO) < rop)` and skips if false. This is a critical bug leading to overstocking.
- **Security Risks:**
  - **Hardcoded Secrets:** `appsettings.json` contains plain text passwords for SQL Server (`sa`/`1`) and Logo API (`5346`). These must be removed immediately.
- **Architectural Flaws:**
  - **N+1 Database Connections:** `ProcessMrpCommandHandler.cs` iterates through items and calls `_stockRepository` methods (`GetItemRefAndUnitByCodeAsync`, `GetStockQuantityAsync`, etc.) for *each item*. Each repository method opens and closes a new SQL connection. This will cause connection pool exhaustion under load.

#### ⚠️ High Priority (Performance & Best Practices)
- **N+1 Problems:**
  - As noted above, the loop in `ProcessMrpCommandHandler.cs` performs multiple database round-trips per item.
- **Configuration:**
  - **Dynamic SQL Injection Risk:** `StockRepository.cs` constructs table names using `firmNo` and `periodNr` directly from inputs (`LG_{firmNo}_{periodNr}_...`). While these come from headers/config, lack of strict validation (e.g., regex `^\d+$`) poses a potential SQL Injection risk if headers are manipulated.
- **Logic Mismatches (Minor):**
  - **Fiche No Generation:** `GetLastMRPNumberAsync` falls back to `MRPyyyyMM-00001` (5 digits) if no record exists, whereas legacy code falls back to `MRPyyyyMM-000001` (6 digits). This might cause sequence issues if legacy data exists.

#### ✅ Verification Success
- **MRP Formula:** The formula `Need = Incoming - (ROP - Stock - OpenPO)` is mathematically equivalent in both systems.
  - Legacy: `AMOUNT = amount - (rop - invTotal - openPO)`
  - New: `need = amount - (ROP - (Stock + OpenPO))`
- **Type Logic:** The handling of Raw Material (10) vs Semi/Finished (11/12) correctly maps to ClientRef vs BomMasterRef/BomRevRef.
- **Wolverine Implementation:** `Program.cs` correctly uses `builder.Host.UseWolverine()` and Controllers inject `IMessageBus`. No MediatR artifacts found.
- **Basic Auth:** `LogoClientService` correctly implements Basic Auth using the provided API Key.

#### 💡 Recommendations

**1. Fix Logic in `ProcessMrpCommandHandler.cs`:**
```csharp
// Current:
var ropGap = item.ROP - netStock;
double need = item.ROP_update_OrderQuantity - ropGap;
if (need > 0 && item.PlanningType == "MTS") { ... }

// Fix:
// Explicitly check for shortage first, as per Legacy Code
if (netStock < item.ROP && item.PlanningType == "MTS")
{
    var ropGap = item.ROP - netStock; // Positive since netStock < ROP
    double need = item.ROP_update_OrderQuantity - ropGap;

    if (need > 0)
    {
         // Add transaction
    }
}
```

**2. Optimize Data Access:**
- Implement a `GetItemsStockDataAsync(List<string> itemCodes)` method in `StockRepository` that fetches all required data (Ref, Stock, OpenPO, Type) in a single or few batch queries using `WHERE CODE IN (...)`.
- Use a single `SqlConnection` for the scope of the request (already Scoped, but open/close per method prevents reuse if not managed carefully). Better to use a Unit of Work pattern or pass the connection/transaction to repo methods.

**3. Security Hardening:**
- Move passwords to User Secrets or Environment Variables.
- Validate `firmNo` and `periodNr` using Regex `^[0-9]{3}$` and `^[0-9]{2}$` respectively before using them in SQL.

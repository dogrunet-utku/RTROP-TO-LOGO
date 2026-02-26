# Change Log

## [2026-02-25]
- Resolved race condition between Serilog table creation and EF Core migrations.
- Updated `20260218004423_AddSerilogExtendedColumns.cs` to use idempotent Raw SQL.
- Set `autoCreateSqlTable` to `true` in `appsettings.json`.
- "Serilog and EF Core migration conflict resolved by making the AddSerilogExtendedColumns migration idempotent via Raw SQL."

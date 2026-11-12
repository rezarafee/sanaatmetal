# CLAUDE.md

All project instructions for AI agents live in **[AGENTS.md](AGENTS.md)** — read it before making changes.

Quick reminders:
- ASP.NET Core 10 MVC + EF Core (SQL Server; SQLite for quick runs), Identity with a role/permission matrix.
- Public site fa (RTL, default) / en (LTR); admin panel is Persian-only RTL.
- Strict CSP: no inline scripts or `onclick`; JS goes in `wwwroot/js`.
- Shared Plesk hosting cannot write to the site root: use the existing fallbacks (`WritableDir`, `FileStorageService` → `StoredFiles`).
- The owner writes in Persian; answer in Persian.
- Build: `dotnet build src/SanaatMetal`. Run: `ASPNETCORE_ENVIRONMENT=Development Database__Provider=Sqlite dotnet run` in `src/SanaatMetal` (login `superadmin` / `Admin@123456`).

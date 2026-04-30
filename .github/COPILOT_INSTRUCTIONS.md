Goal
- Assist with code changes in this repository (Blazor app, .NET 10, C# 14) while preserving project conventions.

Repository summary
- App type: Blazor (prefer Blazor patterns over Razor Pages/MVC).
- Target framework: .NET 10, C# 14 — do not change TFM or LangVersion.
- Key projects: `PlainSight.Server`, `PlainSight.Player`, `PlainSight.AppHost`.
- App host creates runtime resources (Postgres + db) and wires `PlainSight_Server`.

Before making edits
1. Read files reported by `git status` and any open files in the IDE.
2. Build the solution: `dotnet build`.
3. Run tests (if present): `dotnet test`.

Coding rules
- Prefer least visibility: `private`/`internal` before `public`.
- Use modern C# idioms allowed by C# 14 and .NET 10 (file-scoped namespaces, `ArgumentNullException.ThrowIfNull`, `Async` suffix for async methods).
- Add null/argument guards: `ArgumentNullException.ThrowIfNull(x)` and `string.IsNullOrWhiteSpace` for strings.
- Async methods should accept `CancellationToken` where appropriate and pass it through.
- No silent catches — log and rethrow or return errors explicitly.
- Keep diffs minimal and focused; avoid unrelated formatting changes.

Code Style

**C# Specific:**
- **Always use explicit types; never use `var`** - This is a hard requirement
- Follow .NET naming conventions (PascalCase for public members, camelCase for private)
- Use async/await properly - don't block on async code
- Dispose IDisposable objects properly (using statements)
- Keep methods focused and single-purpose

**General:**
- Write clear, readable code with meaningful variable and function names
- Comment complex logic, not obvious code
- Follow existing code patterns and conventions in the project
- Keep functions small and focused on a single task
- Write self-documenting code where possible
- Use LINQ for collections where appropriate

**Documentation Requirements:**
- Update markdown files when changing functionality
- Document new features with clear examples
- Keep documentation in sync with code changes
- Use clear, concise language
- Ensure documentation is accessible for Pathfinders (10-15 years old) and leaders

APIs, DI and tests
- Do not add new public interfaces unless required for DI or testing.
- When adding public behavior, include unit tests matching the repo's test style.

Database and infra
- App host modifies runtime resources (Postgres). Keep `ContainerLifetime.Persistent` unless instructed otherwise.
- When changing EF schema, coordinate migrations and tests against local DB only after confirming safe state.

Commits and PRs
- One logical change per commit with a descriptive message.
- Include the user's uncommitted edits in the branch or call them out in the PR description.

If blocked or uncertain
- Inspect `git status` and the open files listed by the IDE. Ask one concise question only when necessary.

Files to read first (priority)
- `src/PlainSight.AppHost/AppHost.cs`
- Any files reported by `git status`
- `src/PlainSight.Server/Data/PlainSightDbContext.cs`
- `src/PlainSight.Server/Program.cs`

Deliverables
- Small focused patches that compile and pass tests.
- A short plan before multi-file changes.
- Simple verification steps (build/test commands).
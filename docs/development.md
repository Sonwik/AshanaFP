# Developer guide

## Requirements

* .NET 10 SDK. Any editor works; Visual Studio 2026, Rider and VS Code with C# Dev Kit understand the solution.
* Node.js 20+ only for the end-to-end tests and the two asset scripts in `tools/`.

```bash
dotnet build            # whole solution, warnings are treated seriously (analyzers + code style)
dotnet run --project src/Ashana.Web
dotnet watch --project src/Ashana.Web     # hot reload while editing Razor and C#
```

## Configuration

Settings live in [`src/Ashana.Web/appsettings.json`](../src/Ashana.Web/appsettings.json). Any value can be
overridden on the command line (`-- --Storage:Provider=Memory`) or with an environment variable
(`Storage__Provider=Memory`).

| Key | Default | Meaning |
|---|---|---|
| `Canteen:Name` | `Ashana Canteen` | Shown in the interface. |
| `Canteen:TimeZone` | `Asia/Almaty` | IANA time zone of the canteen. The server itself may run in UTC. |
| `Canteen:FallbackUtcOffsetHours` | `5` | Used if the operating system does not know the time zone. |
| `Storage:Provider` | `File` | `File` keeps data in a JSON file; `Memory` keeps nothing after a restart. |
| `Storage:FilePath` | `App_Data/ashana-data.json` | Relative to the web project folder. |
| `Seed:DemoData` | `true` | Create demo users, menu and history when the store is empty. |
| `Seed:HistoryDays` | `14` | Days of order history for the dashboard. |
| `Seed:ResetOnStart` | `false` | Delete everything and create fresh demo data at startup. |
| `Worker:Enabled` | `true` | Background job: marks uncollected orders, refills daily stock, closes yesterday's orders. |
| `Worker:IntervalSeconds` | `30` | How often the job runs (at least 5). |
| `RateLimits:AuthPerMinute` | `10` | Sign-in and sign-up attempts per IP address per minute. |
| `RateLimits:ApiPerMinute` | `120` | JSON API calls per IP address per minute. |

Canteen rules (opening hours, slot length, capacity, lead time, limits) are data, not configuration: an
administrator changes them on **Admin → Settings** without restarting the app.

## Conventions

* **Dependencies point inwards.** Functional references nothing; Domain references Functional; Application references
  Domain; Infrastructure and Web implement the interfaces Application defines. Project references enforce this.
* **Functional core, imperative shell.** Decisions are pure functions (`*Workflow` classes, domain records): they take
  the state, the input and the time as arguments and return `Result<Change<T>>`. Services only read the clock, run the
  transition through `IStateStore`, then log and publish events.
* **Immutability.** Entities are records with `private init` setters and a validating `Create` factory. A change
  returns a new record (`order.Advance(now)` returns `Result<Order>`); never write code that expects the old value to change.
* **Expected failures are values.** Methods that can fail for business reasons return `Result<T>` (`Result<Unit>` for
  "no value") with an `Error` (`Code`, `Message`, `Kind`). Exceptions are for bugs and broken infrastructure only.
* **No null for "not found".** Lookups return `Option<T>` (`state.FindOrder(id)`); convert with `.ToResult(error)`.
* **Rules as data.** New validation rules go into the rule lists (`Order.DraftRules`, `MenuItem.Rules`,
  `CanteenSettings.RuleChecks`, `OrderWorkflow.CommandRules`).
* **Every edit bumps `Version`** (`this with { ..., Version = Version + 1 }`). Forms send the version they loaded; the
  workflow fails with a conflict error if somebody else changed the record in the meantime.
* **One writer at a time.** `IStateStore.UpdateAsync` runs transitions one after another on the latest state, so two
  orders can never sell the same portion. Readers use `store.Current` without locks.
* **Services are stateless singletons.** All data lives in the state snapshot.
* **Events after committing.** Services publish `OrderChanged`, `StockChanged`, `MenuChanged`, `SettingsChanged`
  (cases of the `AppEvent` union) after a successful transition; pages and the SignalR broadcaster subscribe.
* **Time comes from `CanteenClock`** (which wraps `TimeProvider`), never from `DateTime.Now`, so tests control it.
* **The UI library knows no business types.** `Ashana.UI` components take strings, numbers and render fragments, so
  they can be reused in another app.
* **CSS isolation.** Each component has a `.razor.css` file. Elements created by JavaScript do not carry the scope
  attribute; style them through `::deep` (see `Board.razor.css`).
* **No inline scripts.** The Content-Security-Policy allows scripts from our own origin only. Put JavaScript in
  `wwwroot/js` or the UI library's module (`ashana-ui.js`) and call it through `AshanaJs`.

## Recipes

### Add a page

1. Create `src/Ashana.Web/Components/Pages/Something.razor` with `@page "/something"`.
2. Protect it: `@attribute [Authorize]`, or `[Authorize(Policy = Policies.Kitchen)]` / `Policies.Admin`.
3. Set the tab title: `<PageTitle>@UiText.Title("Something")</PageTitle>`.
4. Add a link to `TopBar.razor` and `BottomNav.razor` (or to a section layout).
5. Add the path to `tests/e2e/specs/layout.spec.ts` and `accessibility.spec.ts`.

### Add a use case

1. Write the decision as a pure static function `CanteenState → Result<Change<T>>` (for example in a `*Workflow` class);
   put entity rules on the record itself.
2. Add a service method that reads the clock, calls `store.UpdateAsync(state => YourWorkflow(state, ..., now))`,
   then publishes an event with `.Tap(...)` if open pages should update.
3. Test the pure function directly with plain values (see `WorkflowPurityTests`), and the service with `TestApp`.

### Add a component to the UI library

1. Create `src/Ashana.UI/Components/Thing.razor` and `Thing.razor.css`.
2. Keep it free of business types; expose `Class` and `AdditionalAttributes` where it makes sense.
3. Add a test in `tests/Ashana.ComponentTests`.

### Icons and app icons

* Icons are Lucide SVG paths compiled into `IconShapes.g.cs`. To add one, add its name to the list in
  `tools/generate-icons.py` and run it (the usage is at the top of the script).
* PWA icons are rendered from the logo with Playwright (installed by `npm install` in `tests/e2e`):
  `NODE_PATH=tests/e2e/node_modules node tools/render-icons.js src/Ashana.Web/wwwroot/icons`.

## Deployment

```bash
dotnet publish src/Ashana.Web -c Release -o publish
cd publish && ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://0.0.0.0:8080 dotnet Ashana.Web.dll
```

* Keep `App_Data` on a persistent volume (it holds the data file).
* Behind a reverse proxy (nginx, Caddy, a cloud load balancer) allow WebSockets and set
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, so the app sees the original `https` scheme and client IP
  (HTTPS redirection, secure cookies and rate limiting depend on them).
* Sign-in cookies are protected with ASP.NET Core Data Protection keys. Keep the key folder
  (`~/.aspnet/DataProtection-Keys` by default) on a persistent volume, or users are signed out after a redeploy.

The path from one server to many (an event log in a database, a writer per canteen, a SignalR backplane) is described in
[architecture.md](architecture.md#10-scalability).

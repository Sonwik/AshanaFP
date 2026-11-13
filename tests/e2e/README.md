# End-to-end tests

Playwright drives a real Chromium browser through Ashana, on a desktop screen and on a phone (Pixel 7 profile).

```bash
npm install
npx playwright install chromium     # first time only
npm test                            # starts the app with in-memory demo data, runs every spec
npm run test:headed                 # same, with a visible browser window
npm run report                      # opens the HTML report of the last run
```

Playwright starts the app itself with `dotnet run` (port 5199). To test an app that is already running:

```bash
BASE_URL=http://localhost:5191 npm test
```

| Spec | Checks |
|---|---|
| `specs/smoke.spec.ts` | first visit, sign-in for every role, access rules, live TV board, offline page |
| `specs/layout.spec.ts` | no sideways scrolling on 16 pages; bottom navigation on phones |
| `specs/accessibility.spec.ts` | axe-core WCAG 2.1 AA audit of 16 pages and the dark theme |
| `specs/ordering.spec.ts` | student, cook and TV in three browsers at once: the order travels live through every status |

Tests tagged `@interactive` click Blazor components. `npx playwright test --grep @interactive` runs only those;
`--grep-invert @interactive` runs the rest.

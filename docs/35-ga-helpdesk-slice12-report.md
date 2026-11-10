# 35 — GA Helpdesk, R1 slice 12: performance at 500,000 tickets

Status: **implemented and measured** (a load test with 500,000 tickets, 1.2 million timeline entries and 80,000 notifications; the functional suites re-run on the final code). Part of release R1 (see `23-ga-helpdesk-prd.md`): this is the **load-test evidence** the exit checklist asks for, and the fixes it led to. Budgets (PRD §10): p95 read < 300 ms, write < 500 ms, ticket list < 1 s, dashboards < 2 s.

## What was done
1. **A repeatable load rig.** `scripts/load-seed.sql` builds a throw-away database (requesters, agents in eight teams, tickets over two years with a realistic status/priority/SLA mix, timeline entries, long notification inboxes). `scripts/load-test.mjs` signs in as a supervisor, an agent and a requester and runs a weighted mix of 17 request kinds (queues, search, deadline view, ticket detail, both reports, the bell, notifications, catalog, saved views, raising a request, commenting), with optional think time, and prints p50/p95/p99 per request against the budgets (non-zero exit when one is broken).
2. **Measured, found the causes with \`EXPLAIN\` and \`pg_stat_statements\`, fixed them.** Findings and fixes below.
3. **A regression test for a bug the work exposed** (literal search; see 4).

## Findings and fixes
| # | Finding (measured) | Fix |
|---|---|---|
| 1 | Every query carries the tenant filter as \`(@bypass OR tenant_id = @tenant)\`. Once PostgreSQL switches a prepared statement to a *generic* plan it cannot use the tenant indexes for that OR, so counts and lists scanned far more than needed. | The API appends \`-c plan_cache_mode=force_custom_plan\` to the connection string: each statement is planned with its real values (a fraction of a millisecond) and the filter folds away. |
| 2 | The default queue view (everything not finished, newest first) sorted a sequential scan of the table: **353 ms median**, and "any status" pages **540 ms**. | Partial index \`(tenant_id, updated_at DESC, id DESC) WHERE status NOT IN (closed, cancelled, rejected)\` and a full \`(tenant_id, updated_at DESC, id DESC)\`: **19 ms** and **18 ms**. |
| 3 | Search used \`lower(x) LIKE '%…%'\` OR-ed with a per-ticket \`EXISTS\` on users, which forces a sequential scan: **1.85 s** for one search. | Trigram GIN indexes on \`lower(number)\`, \`lower(subject)\`, \`lower(description)\`; the matching people are found first (a few thousand rows) and tickets matched by \`requester_id = ANY(...)\`: **133 ms** (a word in 1 of 15 tickets). |
| 4 | **Bug found by the new test:** the first rewrite escaped \`%\` and \`_\` in the search text but EF's \`Like\` emitted no \`ESCAPE\` clause, so a search for \`%\` found nothing. | Explicit escape character; \`QueueTests.Search_treats_percent_and_underscore_as_ordinary_characters…\` pins it (and the paging totals). |
| 5 | A team member's visibility rule included \`EXISTS(approvals…)\` inside an OR, again defeating the indexes: **agent queue 370 ms**. | The tickets a person was asked to approve are looked up once per request and used as an id list: **54 ms**. |
| 6 | The two reports read up to **20,000 rows into memory** per request and aggregated in C#: **slow, and wrong** for a tenant with more than 20,000 open tickets (the numbers were silently truncated). | Rewritten as database aggregates (one pass over the open tickets grouped by everything the screen needs; per-day, per-priority/team/service groups and averages for the period). Existing exact-number tests pass unchanged. Period indexes on created / resolved / breached. Dashboard **390 → 240 ms**, SLA report **204 → 84 ms**. |
| 7 | Every list counted all matches; for a broad search that is the most expensive part (**400 ms mean**). | A first page that is not full needs no count; otherwise counting stops at **10,000** (\`capped\` in the response, "10,000+" on screen). |
| 8 | A person's notification list sorted tens of thousands of rows. | Index \`(tenant_id, user_id, created_at DESC, id DESC)\`. |
| 9 | Default PostgreSQL memory settings (128 MB shared buffers) made every query slower and the tail much worse. | Not code: documented in the runbook (see below) and used for the measurements. |

## Results (one tenant, 500,000 tickets; PostgreSQL 16 in Docker; API in Release; all on one 8-thread laptop that also ran the load generator)
**One person at a time** (the best case; \`--clients 1\`), median / p95 in ms, before → after:

| Request | Before | After | Budget |
|---|---|---|---|
| Queue, open, 50 rows | 353 / 638 | **19 / 28** | 1000 |
| Queue, any status, deep pages | 540 / 559 | **18 / 34** | 1000 |
| Queue search | 1854 / 2137 | **133 / 260** | 1000 |
| Queue by SLA state, deadline order | 165 / 233 | **48 / 73** | 1000 |
| Queue, agent (team scope) | 370 / 450 | **54 / 79** | 1000 |
| My tickets / ticket detail | 10 / 25 · 13 / 26 | 14 / 20 · 17 / 30 | 300 |
| Operations dashboard / SLA report | 390 / 449 · 204 / 204 | **240 / 296 · 84 / 125** | 2000 |
| Bell, notifications, catalog, approvals count | 4–17 / 9–33 | 5–10 / 8–17 | 300 |
| Raise a request / comment (writes) | 11 / 38 · 11 / 74 | 15 / 21 · 12 / 144 | 500 |

**100 people with a 2-second pause between requests** (about 46 requests/s on the weighted mix, 63 s after a warm-up): **every one of the 17 request kinds is inside its budget**, e.g. queue p95 59 ms, search 406 ms, agent queue 147 ms, ticket detail 62 ms, operations dashboard 494 ms, SLA report 339 ms, writes 73 / 54 ms; no errors.

**Stress: 40 clients that never pause** (93 requests/s, about twice what the unfixed system managed): the queue views, ticket detail, notifications and writes stay inside budget; the heavy ones do not — search p95 5.6 s, operations dashboard 7.2 s, deadline view 1.2 s, agent queue 1.3 s — because the machine's eight cores are all busy running the database. Before the fixes the same stress run gave search 2.5 s (at half the throughput), deadline view 3.6 s, agent queue 2.5 s, dashboard 5.1 s, SLA report 4.5 s, notifications list 785 ms.

## What this does and does not show
- **Shows:** the single-tenant read and write paths at half a million tickets are not algorithmically limited any more: the heavy statements use indexes, nothing scales with the number of rows pulled into memory, and a realistic crowd on a laptop is within budget.
- **Does not show:** 500 concurrent users on dedicated hardware, 100 tenants at once, a million tickets in one tenant (half was tested), the effect of the Next.js proxy in the path (the test calls the API directly), or what a managed PostgreSQL costs. Treat the stress numbers as "where the CPU runs out on this machine", not as a capacity figure, and repeat the run on the real hardware before the pilot (the commands are below).
- Run-to-run variation on a shared laptop is large (a first stress run of the same code gave search p95 9.5 s); the conclusions rest on the plans (\`EXPLAIN\`) and the CPU profile (\`pg_stat_statements\`) as well as the timings.

## Running it again
\`\`\`bash
# 1. a throw-away database and an API started once against it (Release build, proxy key of your choice)
psql -U annaba_digital -d postgres -c "CREATE DATABASE annaba_digital_load"
ConnectionStrings__Default="Host=localhost;Database=annaba_digital_load;Username=…;Password=…" Api__ProxyKey=… Api__Origin=http://localhost:3190 ASPNETCORE_ENVIRONMENT=Development \\
  dotnet run -c Release --project api/src/AnnabaDigital.Api --urls http://localhost:5190
# 2. the data (about 2.5 minutes for 500,000 tickets), then give the agent and requester accounts a login
psql -U annaba_digital -d annaba_digital_load -v n_tickets=500000 -v n_users=2000 -v n_agents=40 -f scripts/load-seed.sql
psql … -c "INSERT INTO user_credentials (user_id, password_hash, password_changed_at, must_change) SELECT u.id, c.password_hash, now(), false FROM users u, user_credentials c WHERE u.email IN ('agent1@load.test','req1@load.test') AND c.user_id = (SELECT id FROM users WHERE email='sarah.wijaya@annabadigital.io')"
# 3. measure
API=http://localhost:5190 PROXY_KEY=… node scripts/load-test.mjs --clients 100 --think 2000 --seconds 60   # a crowd of people
API=http://localhost:5190 PROXY_KEY=… node scripts/load-test.mjs --clients 40 --seconds 40               # stress
\`\`\`
For the costliest statements enable \`pg_stat_statements\` (\`shared_preload_libraries\`) and \`SELECT … FROM pg_stat_statements ORDER BY total_exec_time DESC\`.

## Verification of the functional behaviour
| Check | Result |
|---|---|
| \`dotnet test api/AnnabaDigital.sln\` (PostgreSQL 16, Testcontainers; includes the exact-number report tests that pin the rewritten aggregates and the new literal-search / paging-total test) | **575 passed, 0 failed** |
| \`tsc --noEmit\`, \`eslint\`, \`next build\` | pass |
| Full Playwright suite (light + dark accessibility, responsive) on the final code | **161 passed, 0 failed** |

## Decisions and known gaps
- **Counting stops at 10,000.** The first page of a long list shows "of 10,000+"; paging stays within the first 10,000 results. A person who needs deeper results narrows the filter (date, place, status).
- **Search is substring matching with trigram indexes**, not ranked full-text search; a search of fewer than three characters cannot use the index and is slower on very large tenants. No stemming, no search over answers or comments.
- **The extension \`pg_trgm\` is now required** (see the runbook); the migration fails clearly where it cannot be created.
- **Reports run live over the whole period** (≤ 90 days). The new indexes keep that fast for hundreds of thousands of tickets; pre-aggregated rollups (RPT-008) are the next step if a tenant reaches millions or dashboards are opened by hundreds of people at once.
- **Write amplification:** the queue indexes add maintenance cost to every ticket update (one more entry in two or three indexes). Measured writes stayed at 12–15 ms; the trigram indexes make the *insert* of a ticket with a long description the most expensive write.
- Not tested here: the 1M-ticket list budget exactly, 100 tenants, long-running soak (hours), failover.

## Next
Pilot checklist items that remain from `23-ga-helpdesk-prd.md` §14: privacy and retention policy for ticket data and files (ADM-006), in-app help for the three personas, a restore drill of the files volume together with a database dump, and per-tenant e-mail wording (NTF-002). Or release R2 (work orders, assets, preventive maintenance).

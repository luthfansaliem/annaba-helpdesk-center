-- Bulk data for load testing (NEVER run on a real database). Run against a throw-away database whose API has already started once (so the schema and the
-- starter catalog exist):
--
--   psql -U annaba_digital -d annaba_digital_load -v n_tickets=500000 -v n_users=2000 -v n_agents=40 -f scripts/load-seed.sql
--
-- It adds requesters, agents (role GA Agent) in eight teams, tickets over two years with a realistic status/priority/SLA mix, their timeline events and notifications
-- for the dev accounts, then refreshes the planner statistics. Everything goes into the default tenant, so the dev accounts can sign in and measure.
\set ON_ERROR_STOP on
\timing on
\set tenant '00000000-0000-7000-8000-000000000001'

BEGIN;

-- ---- people
INSERT INTO users (id, email, name, status, email_verified_at, failed_login_count, created_at, updated_at, version, tenant_id)
SELECT gen_random_uuid(), 'req' || g || '@load.test', 'Requester ' || g, 'Active', now(), 0, now(), now(), 1, :'tenant' FROM generate_series(1, :n_users) g;
INSERT INTO users (id, email, name, status, email_verified_at, failed_login_count, created_at, updated_at, version, tenant_id)
SELECT gen_random_uuid(), 'agent' || g || '@load.test', 'Agent ' || g, 'Active', now(), 0, now(), now(), 1, :'tenant' FROM generate_series(1, :n_agents) g;
INSERT INTO user_profiles (user_id, language, timezone, notify_email, notify_security, notify_product, auto_lock_minutes)
SELECT id, 'en', 'Asia/Jakarta', true, true, false, 15 FROM users WHERE email LIKE '%@load.test';
INSERT INTO user_roles (user_id, role_id, assigned_at)
SELECT u.id, r.id, now() FROM users u, roles r WHERE u.email LIKE 'agent%@load.test' AND r.key = 'ga_agent' AND r.tenant_id = :'tenant';
INSERT INTO user_roles (user_id, role_id, assigned_at)
SELECT u.id, r.id, now() FROM users u, roles r WHERE u.email LIKE 'req%@load.test' AND r.key = 'user' AND r.tenant_id = :'tenant';

-- ---- teams
INSERT INTO teams (id, tenant_id, name, created_at, updated_at, version)
SELECT gen_random_uuid(), :'tenant', 'Load team ' || g, now(), now(), 1 FROM generate_series(1, 8) g;
INSERT INTO team_members (team_id, user_id, tenant_id, is_lead, added_at)
SELECT t.id, a.id, :'tenant', (a.rn % 5 = 0), now()
FROM (SELECT id, row_number() OVER (ORDER BY name) rn FROM users WHERE email LIKE 'agent%@load.test') a
JOIN (SELECT id, row_number() OVER (ORDER BY name) rn FROM teams WHERE name LIKE 'Load team %') t ON t.rn = (a.rn % 8) + 1;

-- ---- tickets
CREATE TEMP TABLE svc AS SELECT row_number() OVER (ORDER BY id) rn, id, name FROM services WHERE is_published AND tenant_id = :'tenant';
CREATE TEMP TABLE req AS SELECT row_number() OVER (ORDER BY id) rn, id FROM users WHERE email LIKE 'req%@load.test';
CREATE TEMP TABLE agt AS SELECT row_number() OVER (ORDER BY id) rn, id FROM users WHERE email LIKE 'agent%@load.test';
CREATE TEMP TABLE tm  AS SELECT row_number() OVER (ORDER BY id) rn, id FROM teams WHERE name LIKE 'Load team %';
CREATE TEMP TABLE words AS SELECT * FROM unnest(ARRAY['printer','leak','air conditioning','lamp','chair','parking','badge','courier','paper','door lock','toner','elevator','pantry','meeting room','cleaning']) WITH ORDINALITY AS w(word, rn);

CREATE TEMP TABLE gen AS
SELECT g,
       now() - random() * 730 * interval '1 day' AS created_at,
       random() AS rs, random() AS rp, random() AS rq,
       1 + floor(random() * (SELECT count(*) FROM svc))::int AS si,
       1 + floor(random() * :n_users)::int AS ri,
       1 + floor(random() * :n_agents)::int AS ai,
       1 + floor(random() * 8)::int AS ti,
       1 + floor(random() * 15)::int AS wi
FROM generate_series(1, :n_tickets) g;

INSERT INTO tickets (id, tenant_id, number, year, sequence, service_id, service_name, form_version, subject, description, answers_json, requester_id, created_by_id, priority, status,
                     assignee_id, team_id, resolved_at, closed_at, reopen_count, created_at, updated_at, version, resolution_due_at, sla_state, assigned_at, first_responded_at)
SELECT gen_random_uuid(), :'tenant',
       'GA-' || s.yr || '-' || lpad(s.seq::text, 6, '0'), s.yr, s.seq,
       sv.id, sv.name, 1,
       initcap(w.word) || ' problem ' || s.g, 'Please help with the ' || w.word || ' near the ' || (ARRAY['lobby','second floor','warehouse','pantry','reception'])[1 + (s.g % 5)] || '.',
       '{"what":"x"}'::jsonb, r.id, r.id,
       CASE WHEN s.rp < 0.15 THEN 'Low' WHEN s.rp < 0.75 THEN 'Medium' WHEN s.rp < 0.95 THEN 'High' ELSE 'Urgent' END,
       s.status,
       CASE WHEN s.status = 'Submitted' THEN NULL ELSE a.id END, tm.id,
       CASE WHEN s.status IN ('Resolved', 'Closed') THEN s.created_at + interval '2 days' END,
       CASE WHEN s.status = 'Closed' THEN s.created_at + interval '4 days' END,
       CASE WHEN s.status = 'Reopened' THEN 1 ELSE 0 END,
       s.created_at, LEAST(now(), s.created_at + random() * 20 * interval '1 day'), 1,
       CASE WHEN s.status IN ('Submitted', 'Assigned', 'InProgress', 'OnHold', 'Reopened') THEN s.created_at + interval '3 days' END,
       CASE WHEN s.status IN ('Resolved', 'Closed') THEN (CASE WHEN s.rq < 0.9 THEN 'Met' ELSE 'Breached' END)
            WHEN s.status IN ('Submitted', 'Assigned', 'InProgress', 'OnHold', 'Reopened') THEN (CASE WHEN s.rq < 0.6 THEN 'OnTrack' WHEN s.rq < 0.8 THEN 'AtRisk' WHEN s.rq < 0.93 THEN 'Breached' ELSE 'Paused' END)
            ELSE 'None' END,
       CASE WHEN s.status <> 'Submitted' THEN s.created_at + interval '1 hour' END,
       CASE WHEN s.status NOT IN ('Submitted', 'Cancelled') THEN s.created_at + interval '2 hours' END
FROM (SELECT gen.*, EXTRACT(year FROM created_at)::int AS yr, row_number() OVER (PARTITION BY EXTRACT(year FROM created_at) ORDER BY created_at, g) AS seq,
             CASE WHEN rs < 0.70 THEN 'Closed' WHEN rs < 0.78 THEN 'Resolved' WHEN rs < 0.86 THEN 'InProgress' WHEN rs < 0.90 THEN 'Assigned' WHEN rs < 0.93 THEN 'OnHold'
                  WHEN rs < 0.96 THEN 'Submitted' WHEN rs < 0.98 THEN 'Cancelled' ELSE 'Reopened' END AS status
      FROM gen) s
JOIN svc sv ON sv.rn = s.si JOIN req r ON r.rn = s.ri JOIN agt a ON a.rn = s.ai JOIN tm ON tm.rn = s.ti JOIN words w ON w.rn = s.wi;

INSERT INTO ticket_counters (tenant_id, year, last)
SELECT :'tenant', year, max(sequence) FROM tickets WHERE tenant_id = :'tenant' GROUP BY year
ON CONFLICT (tenant_id, year) DO UPDATE SET last = GREATEST(ticket_counters.last, EXCLUDED.last);

-- ---- timeline: a creation entry for everybody, a status entry unless still submitted, and a public comment on a third of them
INSERT INTO ticket_events (id, tenant_id, ticket_id, kind, actor_id, actor_label, internal, body, created_at)
SELECT gen_random_uuid(), tenant_id, id, 'Created', requester_id, 'Requester', false, NULL, created_at FROM tickets WHERE tenant_id = :'tenant' AND subject LIKE '% problem %';
INSERT INTO ticket_events (id, tenant_id, ticket_id, kind, actor_label, internal, from_status, to_status, created_at)
SELECT gen_random_uuid(), tenant_id, id, 'Status', 'System', false, 'Submitted', status, created_at + interval '1 hour' FROM tickets WHERE tenant_id = :'tenant' AND status <> 'Submitted' AND subject LIKE '% problem %';
INSERT INTO ticket_events (id, tenant_id, ticket_id, kind, actor_id, actor_label, internal, body, created_at)
SELECT gen_random_uuid(), tenant_id, id, 'Comment', assignee_id, 'Agent', (abs(hashtext(id::text)) % 4 = 0), 'We are looking into it.', created_at + interval '3 hours'
FROM tickets WHERE tenant_id = :'tenant' AND assignee_id IS NOT NULL AND abs(hashtext(id::text)) % 3 = 0 AND subject LIKE '% problem %';

-- ---- notifications: a long inbox for the dev accounts and a short one for the rest
INSERT INTO notifications (id, tenant_id, user_id, kind, title, body, link, ticket_id, created_at, read_at)
SELECT gen_random_uuid(), :'tenant', u.id, 'ticket.assigned', 'GA-2026-' || g || ' was assigned to you', 'Load notification', '/tickets', NULL, now() - (g || ' minutes')::interval, CASE WHEN g % 4 = 0 THEN NULL ELSE now() END
FROM users u, generate_series(1, 20000) g WHERE u.email IN ('sarah.wijaya@annabadigital.io', 'rizky.pratama@annabadigital.io');
INSERT INTO notifications (id, tenant_id, user_id, kind, title, body, link, ticket_id, created_at, read_at)
SELECT gen_random_uuid(), :'tenant', u.id, 'ticket.started', 'Work has started', 'Load notification', '/tickets', NULL, now() - (g || ' hours')::interval, CASE WHEN g % 3 = 0 THEN NULL ELSE now() END
FROM users u, generate_series(1, 20) g WHERE u.email LIKE 'req%@load.test';

COMMIT;
ANALYZE;
SELECT 'tickets' AS what, count(*) FROM tickets UNION ALL SELECT 'ticket_events', count(*) FROM ticket_events UNION ALL SELECT 'notifications', count(*) FROM notifications UNION ALL SELECT 'users', count(*) FROM users;

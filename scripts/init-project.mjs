#!/usr/bin/env node
/**
 * Turns this template into a new project: renames the product everywhere (display name, .NET projects and namespaces,
 * database, containers, seed e-mail domain, deploy hosts) in one pass.
 *
 *   node scripts/init-project.mjs                       (asks for what it needs)
 *   node scripts/init-project.mjs --name "Acme Portal" --domain acme.io --yes
 *   node scripts/init-project.mjs --dry-run --name "Acme Portal" --domain acme.io
 *
 * Flags: --name <Display Name>   --slug <kebab-case>   --domain <example.com>   --staging-host <host>
 *        --vps-host <user@host>  --repo <owner/name>   --yes (no prompts)   --dry-run   --keep-script
 *
 * Run it once, on a fresh copy of the template, from the repository root. It only touches files tracked by git.
 * It does NOT replace the logo artwork (see "Replace the logo" in the README), secrets, or the git remote.
 */
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, renameSync, rmSync, writeFileSync, statSync } from "node:fs";
import { dirname, join, basename } from "node:path";
import { createInterface } from "node:readline/promises";
import { fileURLToPath } from "node:url";

// What the template ships with. Change these only if you rename the template itself.
const FROM = { name: "Annaba Digital", pascal: "AnnabaDigital", slug: "annaba-digital", snake: "annaba_digital", flat: "annabadigital", domain: "annabadigital.io", stagingHost: "template-staging.annabadigital.io", vpsHost: "dev@187.77.159.9", repo: "luthfansaliem/annaba-application-template", pkg: "annaba-application-template" };

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const self = "scripts/init-project.mjs";

const argv = process.argv.slice(2);
const flag = (n) => { const i = argv.indexOf(`--${n}`); return i >= 0 ? argv[i + 1] : undefined; };
const has = (n) => argv.includes(`--${n}`);
const dry = has("dry-run"), yes = has("yes");

const die = (m) => { console.error(`\n✖ ${m}\n`); process.exit(1); };
const git = (...a) => execFileSync("git", a, { cwd: root, encoding: "utf8", maxBuffer: 1 << 28 });

if (!existsSync(join(root, ".git"))) die("Run this inside the git checkout of the template (it only edits tracked files).");
if (!existsSync(join(root, "api", "AnnabaDigital.sln"))) die("This looks like it was already initialised (api/AnnabaDigital.sln is gone). Nothing to do.");
if (!dry && git("status", "--porcelain").trim()) die("The working tree has uncommitted changes. Commit or stash them first so the rename is one clean, reviewable diff.");

const rl = createInterface({ input: process.stdin, output: process.stdout });
async function ask(label, given, dflt, check) {
  let v = given;
  while (true) {
    if (v === undefined) v = yes ? dflt : (await rl.question(`${label}${dflt ? ` [${dflt}]` : ""}: `)).trim() || dflt;
    const err = v ? check(v) : "A value is required.";
    if (!err) return v;
    if (yes || given !== undefined) die(`${label}: ${err}`);
    console.log(`  ✖ ${err}`); v = undefined;
  }
}

const toPascal = (s) => s.split(/[^A-Za-z0-9]+/).filter(Boolean).map((w) => w[0].toUpperCase() + w.slice(1)).join("");
const toSlug = (s) => s.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");

const name = await ask("Product display name (e.g. Acme Portal)", flag("name"), undefined,
  (v) => /^[A-Za-z0-9][A-Za-z0-9 .&-]{1,60}$/.test(v) ? "" : "Use 2–61 letters, digits, spaces, '.', '&' or '-' (the name is written into code strings).");
const slug = await ask("Slug (lowercase, used for containers, package and folders)", flag("slug"), toSlug(name),
  (v) => /^[a-z][a-z0-9-]{1,40}$/.test(v) && !v.endsWith("-") ? "" : "Use lowercase letters, digits and '-', starting with a letter.");
const domain = await ask("Primary domain for e-mail and seed users (e.g. acme.io)", flag("domain"), `${slug.replace(/-/g, "")}.example`,
  (v) => /^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$/.test(v) ? "" : "Enter a bare domain like acme.io.");
const stagingHost = await ask("Staging host", flag("staging-host"), `staging.${domain}`, (v) => /^[a-z0-9.-]+$/.test(v) ? "" : "Enter a bare host name.");
const vpsHost = await ask("VPS ssh target (user@host)", flag("vps-host"), "deploy@your-vps-host", (v) => /^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+$/.test(v) ? "" : "Use user@host.");
const repo = await ask("GitHub repo (owner/name)", flag("repo"), `your-org/${slug}`, (v) => /^[\w.-]+\/[\w.-]+$/.test(v) ? "" : "Use owner/name.");
rl.close();

const pascal = toPascal(name);
if (!/^[A-Z][A-Za-z0-9]{1,40}$/.test(pascal)) die(`Cannot derive a valid .NET namespace from "${name}" (got "${pascal}"). Start the name with a letter.`);
const to = { name, pascal, slug, snake: slug.replace(/-/g, "_"), flat: slug.replace(/-/g, ""), domain, stagingHost, vpsHost, repo, pkg: slug };

// One pass over the text with a longest-match-first alternation, so a replacement is never scanned again
// (a new domain that happens to contain an old token cannot be rewritten twice).
const MAP = new Map([
  ["Annaba Digital Indonesia", to.name],
  [FROM.repo, to.repo],
  [FROM.vpsHost, to.vpsHost],
  [FROM.stagingHost, to.stagingHost],
  [FROM.domain, to.domain],
  [FROM.pascal, to.pascal],
  ["Annaba%20Digital", encodeURIComponent(to.name)],
  [FROM.name, to.name],
  [FROM.slug, to.slug],
  [FROM.snake, to.snake],
  ["annaba-vps-backups", `${to.slug}-vps-backups`],
  [FROM.flat, to.flat],
  [FROM.pkg, to.pkg],
]);
const RULES = [...MAP.keys()].map((k) => [k]);
const esc = (x) => x.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
const RE = new RegExp([...MAP.keys()].sort((a, b) => b.length - a.length).map(esc).join("|"), "g");
const rewrite = (s) => s.replace(RE, (m) => MAP.get(m));

const files = git("ls-files", "-z").split("\0").filter(Boolean).filter((f) => f !== self && existsSync(join(root, f)) && statSync(join(root, f)).isFile());
let changed = 0, hits = 0;
const edits = [];
for (const f of files) {
  const buf = readFileSync(join(root, f));
  if (buf.includes(0)) continue; // binary (logo PNGs, fonts)
  const before = buf.toString("utf8"), after = rewrite(before);
  if (after !== before) { changed++; hits += RULES.reduce((n, [a]) => n + before.split(a).length - 1, 0); edits.push([f, after]); }
}

// Paths: rename deepest first (files, then folders) so parents still exist.
const paths = new Set();
for (const f of files) { const parts = f.split("/"); for (let i = 1; i <= parts.length; i++) paths.add(parts.slice(0, i).join("/")); }
const renames = [...paths].filter((p) => rewrite(basename(p)) !== basename(p)).sort((a, b) => b.split("/").length - a.split("/").length);

console.log(`\n${dry ? "DRY RUN — nothing will be written.\n" : ""}${FROM.name} → ${to.name}`);
console.log(`  namespace ${FROM.pascal} → ${to.pascal}   slug ${FROM.slug} → ${to.slug}   db ${FROM.snake} → ${to.snake}`);
console.log(`  domain ${FROM.domain} → ${to.domain}   staging ${FROM.stagingHost} → ${to.stagingHost}`);
console.log(`  vps ${FROM.vpsHost} → ${to.vpsHost}   repo ${FROM.repo} → ${to.repo}`);
console.log(`  ${changed} files change (${hits} replacements), ${renames.length} paths are renamed.\n`);
if (dry) process.exit(0);

for (const [f, text] of edits) writeFileSync(join(root, f), text);
for (const p of renames) renameSync(join(root, p), join(root, dirname(p), rewrite(basename(p))));

// README: drop the "Using this template" section (between the markers) from the new project.
const readme = join(root, "README.md");
if (existsSync(readme)) {
  const t = readFileSync(readme, "utf8");
  const cleaned = t.replace(/<!-- template:start -->[\s\S]*?<!-- template:end -->\n*/g, "");
  if (cleaned !== t) writeFileSync(readme, cleaned);
}
if (!has("keep-script")) rmSync(join(root, self));

console.log("Done. Next steps:");
console.log("  1. Replace the logo: public/brand/*.png and src/app/icon.png (same file names; see README). The theme colour is in src/app/globals.css.");
console.log("  2. Review the diff (git diff --stat), then run: cd api && dotnet test && cd .. && npm ci && npx tsc --noEmit && npx vitest run");
console.log("  3. Set your own secrets (deploy/env.example lists them); never reuse another project's keys or database.");
console.log("  4. Point git at the new repo, commit, and push.");
console.log(`  5. docs/22-staging-runbook.md is the template's own project history — prune or rewrite it for ${to.name}.\n`);

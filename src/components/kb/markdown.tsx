import { Fragment, type ReactNode } from "react";

/**
 * A small, safe Markdown renderer for knowledge-base articles. It builds React elements (never HTML strings), so nothing in an article can inject markup or script.
 * Supported: headings (#–###), paragraphs, lists (- * 1.), quotes (>), fenced code, rules (---), **bold**, *italic* / _italic_, `code`, [links](https://…, /…, mailto:)
 * and images of the article's own files: ![alt](file:<id>). Anything else is shown as plain text.
 */
export function Markdown({ text, fileUrl }: { text: string; fileUrl?: (id: string) => string }) {
  return <div className="kb-body">{blocks(text.replace(/\r\n/g, "\n"), fileUrl)}</div>;
}

const SAFE_LINK = /^(https?:\/\/|mailto:|\/(?!\/))/i;

function blocks(src: string, fileUrl?: (id: string) => string): ReactNode[] {
  const lines = src.split("\n"); const out: ReactNode[] = []; let i = 0; let key = 0;
  const inl = (s: string) => inline(s, fileUrl);
  while (i < lines.length) {
    const line = lines[i];
    if (!line.trim()) { i++; continue; }
    if (line.startsWith("```")) {
      const code: string[] = []; i++;
      while (i < lines.length && !lines[i].startsWith("```")) code.push(lines[i++]);
      i++; out.push(<pre key={key++}><code>{code.join("\n")}</code></pre>); continue;
    }
    const h = /^(#{1,3})\s+(.*)$/.exec(line);
    if (h) { const level = h[1].length; const Tag = (["h2", "h3", "h4"] as const)[level - 1]; out.push(<Tag key={key++}>{inl(h[2])}</Tag>); i++; continue; }
    if (/^(-{3,}|\*{3,})\s*$/.test(line)) { out.push(<hr key={key++} />); i++; continue; }
    if (line.startsWith(">")) {
      const q: string[] = [];
      while (i < lines.length && lines[i].startsWith(">")) q.push(lines[i++].replace(/^>\s?/, ""));
      out.push(<blockquote key={key++}>{blocks(q.join("\n"), fileUrl)}</blockquote>); continue;
    }
    if (/^\s*([-*])\s+/.test(line) || /^\s*\d+[.)]\s+/.test(line)) {
      const ordered = /^\s*\d+[.)]\s+/.test(line); const items: string[] = [];
      const re = ordered ? /^\s*\d+[.)]\s+(.*)$/ : /^\s*[-*]\s+(.*)$/;
      while (i < lines.length && re.test(lines[i])) items.push(re.exec(lines[i++])![1]);
      const li = items.map((t, n) => <li key={n}>{inl(t)}</li>);
      out.push(ordered ? <ol key={key++}>{li}</ol> : <ul key={key++}>{li}</ul>); continue;
    }
    const para: string[] = [];
    while (i < lines.length && lines[i].trim() && !/^(#{1,3}\s|```|>|\s*[-*]\s+|\s*\d+[.)]\s+|-{3,}\s*$)/.test(lines[i])) para.push(lines[i++]);
    if (para.length === 0) para.push(lines[i++]);
    out.push(<p key={key++}>{para.map((p, n) => <Fragment key={n}>{n > 0 && <br />}{inl(p)}</Fragment>)}</p>);
  }
  return out;
}

const TOKEN = /(`[^`]+`)|(!\[[^\]]*\]\([^)\s]+\))|(\[[^\]]+\]\([^)\s]+\))|(\*\*[^*]+\*\*)|(\*[^*\s][^*]*\*|_[^_\s][^_]*_)/;

function inline(s: string, fileUrl?: (id: string) => string): ReactNode[] {
  const out: ReactNode[] = []; let rest = s; let key = 0;
  while (rest) {
    const m = TOKEN.exec(rest);
    if (!m) { out.push(rest); break; }
    if (m.index > 0) out.push(rest.slice(0, m.index));
    const tok = m[0];
    if (m[1]) out.push(<code key={key++}>{tok.slice(1, -1)}</code>);
    else if (m[2]) {
      const [, alt, target] = /^!\[([^\]]*)\]\(([^)\s]+)\)$/.exec(tok)!;
      const file = /^file:([0-9a-f-]{36})$/i.exec(target);
      // eslint-disable-next-line @next/next/no-img-element
      out.push(file && fileUrl ? <img key={key++} src={fileUrl(file[1])} alt={alt} loading="lazy" style={{ maxWidth: "100%" }} /> : <span key={key++}>[{alt || "image"}]</span>);
    } else if (m[3]) {
      const [, label, href] = /^\[([^\]]+)\]\(([^)\s]+)\)$/.exec(tok)!;
      out.push(SAFE_LINK.test(href) ? <a key={key++} href={href} {...(/^https?:/i.test(href) ? { target: "_blank", rel: "noreferrer noopener" } : {})}>{label}</a> : <span key={key++}>{label}</span>);
    } else if (m[4]) out.push(<strong key={key++}>{inline(tok.slice(2, -2), fileUrl)}</strong>);
    else out.push(<em key={key++}>{inline(tok.slice(1, -1), fileUrl)}</em>);
    rest = rest.slice(m.index + tok.length);
  }
  return out;
}

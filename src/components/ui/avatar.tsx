const palettes: [string, string][] = [
  ["#f6a35a", "#c2561e"], ["#e98aa5", "#a8325a"], ["#6aa6ff", "#2459c7"], ["#5cc9b0", "#14806a"], ["#a98bf5", "#5b34c4"], ["#7b8aa5", "#3b475f"],
];

/** Same-origin avatar URL; the version busts the cache after an upload. Undefined when the user has no photo. */
export function avatarUrl(id: string, version?: number | null) { return version != null ? `/api/avatars/${id}?v=${version}` : undefined; }

export function Avatar({ name, size = 28, i, src }: { name: string; size?: number; i?: number; src?: string }) {
  const idx = i ?? name.split("").reduce((a, c) => a + c.charCodeAt(0), 0) % palettes.length;
  const [c1, c2] = palettes[idx % palettes.length];
  const initials = name.split(" ").map((w) => w[0]).slice(0, 2).join("");
  if (src) return <span className="avatar" style={{ width: size, height: size, backgroundImage: `url(${src})`, backgroundSize: "cover", backgroundPosition: "center" }} role="img" aria-label={name} />;
  return (
    <span className="avatar" style={{ width: size, height: size, fontSize: size * 0.38, ["--c1" as string]: c1, ["--c2" as string]: c2 }}>
      {initials}
    </span>
  );
}

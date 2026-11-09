import Image from "next/image";

/** Brand artwork lives in /public/brand (transparent PNGs: blue for light theme, white for dark). Both are rendered; CSS shows the one that matches html[data-theme]. */
function Art({ light, dark, ratio, height, alt }: { light: string; dark: string; ratio: number; height: number; alt: string }) {
  const w = Math.round(height * ratio);
  const style = { height, width: "auto" } as const;
  return (
    <>
      <Image className="logo-img logo-img-light" src={light} alt={alt} width={w} height={height} style={style} unoptimized priority />
      <Image className="logo-img logo-img-dark" src={dark} alt="" aria-hidden width={w} height={height} style={style} unoptimized priority />
    </>
  );
}

/** The shield alone (collapsed sidebar). */
export function LogoMark({ size = 34 }: { size?: number }) {
  return <Art light="/brand/mark-light.png" dark="/brand/mark-dark.png" ratio={548 / 664} height={size} alt="Annaba Digital Indonesia" />;
}

/** Shield + "ANNABA DIGITAL INDONESIA". `size` is the height of the shield, so existing callers keep their scale. */
export function Logo({ size = 34 }: { size?: number }) {
  return (
    <div className="logo">
      <Art light="/brand/logo-light.png" dark="/brand/logo-dark.png" ratio={2396 / 532} height={Math.round(size * 1.15)} alt="Annaba Digital Indonesia" />
    </div>
  );
}

export function Building({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 300 200" aria-hidden preserveAspectRatio="xMidYMax meet">
      <defs>
        <linearGradient id="gl" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stopColor="#dff0ff" /><stop offset="1" stopColor="#8cb9e8" /></linearGradient>
        <linearGradient id="gl2" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stopColor="#f2f9ff" /><stop offset="1" stopColor="#a9cdf0" /></linearGradient>
      </defs>
      <polygon points="150,20 270,50 270,200 150,200" fill="url(#gl)" />
      <polygon points="60,70 150,40 150,200 60,200" fill="url(#gl2)" />
      {Array.from({ length: 9 }).map((_, i) => <line key={i} x1="150" x2="270" y1={30 + i * 20 + 0} y2={60 + i * 17} stroke="#fff" strokeOpacity=".7" />)}
      {Array.from({ length: 6 }).map((_, i) => <line key={"v" + i} x1={170 + i * 20} x2={170 + i * 20} y1={30} y2={200} stroke="#fff" strokeOpacity=".6" />)}
      {Array.from({ length: 6 }).map((_, i) => <line key={"w" + i} x1="60" x2="150" y1={80 + i * 22} y2={50 + i * 25} stroke="#fff" strokeOpacity=".7" />)}
      {[30, 60, 90, 120].map((x) => <circle key={x} cx={x} cy={185} r={16} fill="#5fae4b" opacity=".9" />)}
      {[210, 240, 270].map((x) => <circle key={x} cx={x} cy={188} r={14} fill="#79c15a" opacity=".9" />)}
    </svg>
  );
}

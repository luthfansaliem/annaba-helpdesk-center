import { dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { FlatCompat } from "@eslint/eslintrc";
const compat = new FlatCompat({ baseDirectory: dirname(fileURLToPath(import.meta.url)) });
const config = [
  { ignores: [".next/**", ".vercel/**", "node_modules/**", "test-results/**", "playwright-report/**", "api/**", "public/sw.js", "next-env.d.ts", "coverage/**"] },
  ...compat.extends("next/core-web-vitals", "next/typescript"),
];
export default config;
